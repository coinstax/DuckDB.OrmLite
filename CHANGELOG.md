# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-10-01

### Added
- Initial public release
- Complete OrmLite support for DuckDB
- Full CRUD operations (Create, Read, Update, Delete)
- LINQ query expressions
- Transaction support
- AutoIncrement with sequences and INSERT...RETURNING
- Batch operations
- Complex queries (JOINs, aggregations, subqueries)
- Parameterized queries with DuckDB's `$` parameter syntax
- Complete type support:
  - All .NET primitive types
  - DateTime, DateTimeOffset, TimeSpan
  - Decimal and all integer types (TINYINT, SMALLINT, INTEGER, BIGINT, UINTEGER, UBIGINT)
  - Guid (UUID)
  - byte[] (BLOB)
  - Nullable types
- Custom type converters for DuckDB-specific types
- BeforeExecFilter for parameter transformation
- Custom SqlExpression for optimal LINQ support
- 40 comprehensive tests (39 passing - 97.5%)
- SQL injection prevention
- Comprehensive error handling

### Dependencies
- ServiceStack.OrmLite >= 8.5.2
- DuckDB.NET.Data.Full >= 1.3.0
- .NET 8.0

### Documentation
- Complete README with examples
- NuGet publishing guide
- Development history
- Implementation status documentation
- Test coverage recommendations

### Known Limitations
- TimeSpan limited to ~24 hours with HH:MM:SS format
- DuckDB uses single-writer model for concurrent writes

## [1.0.1] - 2025-10-01

### Fixed
- **CRITICAL**: Auto-configure `BeforeExecFilter` in `DuckDbOrmLiteConnectionFactory`
  - Fixed "Values were not provided for the following prepared statement parameters" error
  - The filter is now automatically configured when creating the connection factory
  - Handles DuckDB's parameter name requirements ($ prefix, 1-based indexing)
  - Converts `DbType.Currency` to `DbType.Decimal` automatically

This was a critical bug that prevented v1.0.0 from working in real applications. The tests passed because they had a global filter setup, but applications using the library would fail on any parameterized query.

## [Unreleased]

## [1.3.0] - 2025-10-01

### Added
- **Connection Timeout and Retry** - Automatic retry with exponential backoff for cross-process lock conflicts
  - `Open(TimeSpan timeout)` and `OpenForWrite(TimeSpan timeout)` methods
  - Configurable retry delays via `RetryDelayMs` and `MaxRetryDelayMs` properties
  - Exponential backoff with random jitter to avoid thundering herd
  - Detects DuckDB lock errors: "Could not set lock", "database is locked", "IO Error"
  - Throws `TimeoutException` if lock not acquired within timeout
  - Default: 50ms initial delay, 1000ms max delay

- **Generic Connection Factory** - Type-safe multi-database configuration
  - `DuckDbOrmLiteConnectionFactory<T>` automatically configures multi-database for type T
  - Cleaner syntax: `new DuckDbOrmLiteConnectionFactory<CmcPrice>(...)`

- **Concurrency Testing** - Comprehensive thread safety validation
  - 11 concurrency tests validating MVCC behavior within same process
  - Tests for concurrent reads/writes, update conflicts, and large database operations
  - Verified optimistic concurrency control behavior

### Fixed
- **CRITICAL**: `BeforeExecFilter` isolation - Filter now only applies to DuckDB connections
  - Previously affected ALL OrmLite connections globally (MySQL, SQLite, etc.)
  - Now checks connection type before applying DuckDB-specific transformations
  - Allows safe use of multiple database providers in same application

### Improved
- Test coverage increased to 90 tests (100% passing)
- Documentation updated with timeout/retry examples and concurrency notes
- Better support for multi-process scenarios

### Technical Details
- Cross-process locking: Only one process can open DuckDB file at a time (exclusive lock)
- Within-process concurrency: Multiple threads can read/write via MVCC
- Retry delays are configurable for different contention scenarios
- Filter isolation via connection type checking

### Example
```csharp
// Connection timeout with retry
var factory = new DuckDbOrmLiteConnectionFactory("Data Source=myapp.db")
{
    RetryDelayMs = 100,
    MaxRetryDelayMs = 5000
};
using var db = factory.Open(TimeSpan.FromSeconds(30)); // Retry for 30 seconds

// Generic factory
var factory = new DuckDbOrmLiteConnectionFactory<CryptoPrice>("Data Source=main.db")
    .WithAdditionalDatabases("archive.db");
```

## [1.2.0] - 2025-10-01

### Added
- **Multi-Database Support** 🎉 - Query transparently across multiple DuckDB database files
  - Fluent configuration API: `.WithAdditionalDatabases()` and `.WithMultiDatabaseTables()`
  - Automatic database attachment via `ATTACH` statement
  - Unified view creation with `UNION ALL` across all databases
  - Read/write separation: `Open()` for multi-db reads, `OpenForWrite()` for single-db writes
  - Smart table detection - only creates views for tables that exist
  - Database alias sanitization for file paths with special characters
  - 18 comprehensive multi-database tests (100% passing)

### Use Cases
- **Time-Series Data**: Partition data by year/month across multiple database files
- **Archival Scenarios**: Keep current year writable, historical years as read-only archives
- **Data Lake Queries**: Query across partitioned datasets without application code changes
- **Performance**: Distribute data across files for better read parallelization

### Technical Details
- Separate dialect provider instances for multi-db vs single-db connections
- Automatic view lifecycle management (created on connection open)
- Table existence checking with fallback for missing tables
- Works with all OrmLite operations: SELECT, WHERE, ORDER BY, LIMIT, aggregations, JOINs, async

### Improved
- Test coverage increased to 75 tests (100% passing)
- Zero breaking changes - fully backward compatible

### Limitations
- Tables must have identical schemas across all databases
- Cross-database transactions not supported (use `OpenForWrite()` for transactions)
- `UNION ALL` doesn't deduplicate - ensure partitioning prevents duplicates
- Additional databases should be read-only for data consistency

### Example
```csharp
var factory = new DuckDbOrmLiteConnectionFactory("Data Source=main.db")
    .WithAdditionalDatabases("archive_2024.db", "archive_2023.db")
    .WithMultiDatabaseTables("CmcPrice");

// Queries span all databases automatically
using var db = factory.Open();
var prices = db.Select<CmcPrice>(x => x.Symbol == "BTC");

// Writes go to main database only
using var writeDb = factory.OpenForWrite();
writeDb.Insert(new CmcPrice { ... });
```

See README.md Multi-Database Support section for complete documentation.

## [1.1.0] - 2025-10-01

### Added
- **Async/Await Support** - Full async API support through ServiceStack.OrmLite
  - All CRUD operations: SelectAsync, InsertAsync, UpdateAsync, DeleteAsync, SaveAsync
  - Query operations: SingleAsync, CountAsync, ScalarAsync, ExistsAsync
  - SQL operations: SqlListAsync, SqlScalarAsync, ExecuteNonQueryAsync
  - Transaction support with async operations
  - CancellationToken support throughout
  - 17 comprehensive async tests (100% passing)

### Technical Notes
- Async support is **pseudo-async** (similar to SQLite) since DuckDB.NET.Data v1.3.0 doesn't provide native async I/O
- Operations block the calling thread but provide API compatibility with other OrmLite providers
- Suitable for maintaining consistent async/await code style across your application
- See README.md Async/Await Support section for usage examples and limitations

### Improved
- ServiceStack license now automatically loaded from .env file in tests
- Test coverage increased to 57 tests (100% passing)

### Under Consideration
- Bulk operations optimization using DuckDB's COPY command
- Direct Parquet/CSV operations through OrmLite
- Support for DuckDB-specific types (LIST, STRUCT, MAP)
- Window functions support with LINQ extensions
- DuckDB-specific aggregate functions
- Time series optimizations and ASOF joins
- Query performance profiling integration
- Schema migration tools
- Additional DuckDB configuration options

---

[1.3.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.3.0
[1.2.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.2.0
[1.1.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.1.0
[1.0.1]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.1
[1.0.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.0
