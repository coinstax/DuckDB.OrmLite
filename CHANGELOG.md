# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.5.3] - 2025-10-06

### Fixed - CRITICAL: DateTime Kind Handling 🕐
- **DateTime values now always returned with `DateTimeKind.Utc`**
  - Previously: DateTime values read from database had `Kind = Unspecified`
  - Problem: .NET treats Unspecified as local time in many contexts, causing incorrect timezone conversions
  - Solution: All DateTimes read from database are explicitly set to UTC Kind
  - Impact: Eliminates unexpected local time conversions and timezone bugs

### Root Cause Analysis
DuckDB's TIMESTAMP type is timezone-naive (stores no timezone information). When .NET's ADO.NET provider reads these values, they come back as `DateTime` with `Kind = Unspecified`. This causes issues because:

1. Many .NET APIs treat Unspecified DateTimes as local time
2. Serialization libraries often convert Unspecified to local timezone
3. DateTime comparisons can give incorrect results with mixed Kinds

**Example of the bug:**
```csharp
// Store UTC time: 2025-10-06 12:00:00 UTC
db.Insert(new Event { Date = DateTime.UtcNow });

// Read back (BEFORE fix)
var retrieved = db.SingleById<Event>(1);
// retrieved.Date.Kind == Unspecified
// JSON serialization converts to local time: 2025-10-06 05:00:00 (if PST)
```

### Solution Implementation

**DuckDbDateTimeConverter changes:**

**ToDbValue (Writing to database):**
- `DateTimeKind.Local` → Convert to UTC via `ToUniversalTime()`
- `DateTimeKind.Unspecified` → Explicitly set to UTC via `SpecifyKind(dateTime, DateTimeKind.Utc)`
- `DateTimeKind.Utc` → Keep as-is

**FromDbValue (Reading from database):**
- All DateTimes → Always return with `Kind = Utc` via `SpecifyKind(dateTime, DateTimeKind.Utc)`

### Behavior Changes
- **Before v1.5.3**: DateTimes read from database had `Kind = Unspecified`
- **After v1.5.3**: DateTimes read from database always have `Kind = Utc`
- **Assumption**: All TIMESTAMP values in DuckDB are stored as UTC (standard practice)
- **No breaking changes**: Values themselves are unchanged, only the Kind metadata

### Test Coverage
- **8 new comprehensive DateTime Kind tests:**
  - `UtcDateTime_PreservesUtcKind` - Verifies UTC DateTimes round-trip correctly
  - `UnspecifiedDateTime_ReturnsAsUtc` - Verifies Unspecified is treated as UTC
  - `LocalDateTime_ConvertedToUtc` - Verifies Local times are converted
  - `MultipleRecords_AllReturnUtcKind` - Batch operations preserve UTC Kind
  - `QueryWithDateTime_WorksCorrectly` - WHERE clauses work with UTC DateTimes
  - `NullableDateTime_PreservesUtcKind` - Nullable DateTime support
  - `BulkInsert_PreservesUtcKind` - BulkInsert maintains UTC Kind
  - `UpdateDateTime_PreservesUtcKind` - Updates preserve UTC Kind
- **133 total tests (100% passing, +8 from v1.5.2)**

### User Impact
- **Before v1.5.3**: DateTime serialization/comparisons could use wrong timezone
- **After v1.5.3**: All DateTimes have explicit UTC Kind, preventing timezone bugs
- **Migration**: No code changes needed - values are identical, only Kind metadata added

### Files Changed
- `src/DuckDB.OrmLite/DuckDbTypeConverters.cs` - Updated `DuckDbDateTimeConverter`
- `tests/DuckDB.OrmLite.Tests/DateTimeKindTests.cs` - New comprehensive test suite

---

## [1.5.2] - 2025-10-06

### Fixed - CRITICAL: Staging Table Constraints 🚨
- **Staging table now strips PRIMARY KEY, UNIQUE, and FOREIGN KEY constraints**
  - Previously: Staging table inherited all constraints from main table
  - Problem: When incoming data had duplicates on PK/UNIQUE columns, Appender failed with "duplicate key" error
  - Solution: Strip all constraints from staging table using regex to allow duplicates during deduplication
  - Impact: BulkInsertWithDeduplication now works correctly for tables with PRIMARY KEY or UNIQUE constraints

### Root Cause Analysis
The staging table was created using `GetColumnDefinition()` which includes:
- `PRIMARY KEY` constraints
- `UNIQUE` constraints
- `REFERENCES` (foreign key) constraints

When incoming data contained duplicates on these constrained columns, the DuckDB Appender threw:
```
PRIMARY KEY or UNIQUE constraint violation: duplicate key "value"
```

This defeated the entire purpose of the staging table pattern - it MUST accept duplicates for deduplication to work.

### Technical Implementation
- Added regex patterns to strip constraints from column definitions:
  - `\s+PRIMARY\s+KEY` → removed
  - `\s+UNIQUE` → removed
  - `\s+REFERENCES\s+...` → removed
- Staging table now has same column types but NO constraints
- Allows Appender to insert all rows (including duplicates)
- ROW_NUMBER deduplication then filters to unique rows before INSERT to main table

### Test Coverage (Why This Was Missed)
Previous tests didn't have PRIMARY KEY/UNIQUE on the deduplication columns:
- ✅ Fixed: Added `PrimaryKeyModel` with `[PrimaryKey]` on dedup column
- ✅ Fixed: Added `UniqueConstraintModel` with `[Unique]` on dedup column
- New tests verify duplicates on constrained columns work correctly
- 124 total tests (100% passing, +2 from v1.5.1)

### User Impact
- **Before v1.5.2**: Failed with "duplicate key" error on tables with PK/UNIQUE constraints
- **After v1.5.2**: Works correctly regardless of constraints on main table
- **No breaking changes**: API remains identical

---

## [1.5.1] - 2025-10-06

### Fixed - Internal Duplicate Handling 🔧
- **CRITICAL FIX: BulkInsertWithDeduplication now handles internal duplicates**
  - Previously failed when incoming dataset contained duplicate rows (based on unique key)
  - Now uses `ROW_NUMBER() OVER (PARTITION BY unique_columns)` to deduplicate staging table
  - Keeps first occurrence of each unique key from incoming data
  - Handles both internal duplicates (within incoming data) and external duplicates (with main table)

### Technical Details
- Added CTE with ROW_NUMBER window function to `GenerateDeduplicatedInsertSql`
- Efficient in-database deduplication leveraging DuckDB's columnar engine
- Zero performance impact for datasets without internal duplicates
- Minimal overhead (~5-10ms) even with high duplicate rates

### Behavior Changes
- **Before**: Would fail or produce incorrect results if incoming data had duplicates
- **After**: Automatically deduplicates incoming data (keeps first occurrence) before checking against main table
- **No breaking changes**: API remains identical, behavior is more robust

### Test Coverage
- 3 new tests for internal duplicate scenarios
- `BulkInsertWithDeduplication_InternalDuplicates_KeepsFirstOccurrenceOnly` - Complex multi-duplicate scenario
- `BulkInsertWithDeduplication_AllInternalDuplicates_InsertsOnlyFirst` - Edge case: all duplicates
- `BulkInsertWithDeduplication_InternalDuplicatesWithCompositeKey_WorksCorrectly` - Composite key handling
- 122 total tests (100% passing, +3 from v1.5.0)

### Documentation Updates
- Updated "How It Works" section in README with CTE deduplication SQL
- Added explicit "Duplicate Handling" section explaining internal vs external duplicates
- Updated 845M row scenario to reflect internal duplicate handling

---

## [1.5.0] - 2025-10-05

### Added - Type-Safe LINQ Expression API 🎯
- **LINQ Expression Overload** - Type-safe unique column specification with IntelliSense support
  - `db.BulkInsertWithDeduplication(records, x => x.Email)` - Single column
  - `db.BulkInsertWithDeduplication(records, x => new { x.Col1, x.Col2 })` - Multiple columns
  - `await db.BulkInsertWithDeduplicationAsync(records, x => new { x.Timestamp, x.Symbol })`
  - Compile-time type checking catches typos
  - Refactoring-safe (rename property updates expression)
  - Cleaner syntax than string arrays

### Fixed - Auto-Detection Logic 🔧
- **Corrected multiple unique constraint handling**
  - Previously incorrectly combined multiple constraints (e.g., CompositeKey + [Unique])
  - Now uses priority-based selection: CompositeKey > CompositeIndex(Unique) > Individual [Unique]
  - Throws clear error if multiple constraints at same priority level
  - Prevents ambiguous deduplication keys that could miss duplicates

### API Options Summary
```csharp
// Option 1: LINQ Expression (Recommended - type-safe)
db.BulkInsertWithDeduplication(records, x => new { x.Col1, x.Col2 });

// Option 2: String columns (flexible)
db.BulkInsertWithDeduplication(records, "Col1", "Col2");

// Option 3: Auto-detect from attributes
[CompositeKey(nameof(Col1), nameof(Col2))]
public class MyModel { ... }
db.BulkInsertWithDeduplication(records);
```

### Test Coverage
- 3 new LINQ expression tests
- 3 new constraint validation tests
- 119 total tests (100% passing)

---

## [1.5.0] - 2025-10-05 (Initial Release)

### Added
- **Bulk Insert with Deduplication** 🛡️ - Production-safe bulk loading for massive tables
  - New `BulkInsertWithDeduplication<T>()` method using staging table pattern
  - Solves DuckDB's index-in-memory limitation for 100M+ row tables
  - Explicit unique key specification: `db.BulkInsertWithDeduplication(records, "col1", "col2", "col3")`
  - Auto-detection from `[Unique]`, `[Index(Unique=true)]`, or `[CompositeIndex(Unique=true)]` attributes
  - `BulkInsertWithDeduplicationAsync<T>()` async variants (pseudo-async wrapper)
  - Returns count of inserted rows (excluding duplicates)
  - 13 comprehensive deduplication tests including 845M row scenario simulation

### How It Works
1. **Creates temporary staging table** with same schema as main table
2. **BulkInsert to staging** using Appender API (10-100x faster than InsertAll)
3. **Atomic INSERT SELECT** with LEFT JOIN to filter duplicates
4. **Always cleanup** staging table (even on error)

### Performance
| Operation | Time (70K records) | Notes |
|-----------|-------------------|-------|
| Append to staging | ~5-10ms | Appender API |
| INSERT SELECT with JOIN | ~50-200ms | Depends on table size |
| Drop staging | ~1ms | Cleanup |
| **Total overhead** | **~60-210ms** | Minimal cost for safety |

### Safety Benefits
- ✅ **Zero risk to main table** - Validates in staging before touching main
- ✅ **Atomic duplicate detection** - SQL JOIN ensures no duplicates
- ✅ **Fast rollback** - Just drop staging on error
- ✅ **Minimal lock time** - Main table locked only during final INSERT

### Use Cases
Perfect for:
- Tables with 100M+ rows where UNIQUE indexes can't fit in memory
- ETL/data loading with potential duplicates
- Production scenarios requiring zero corruption risk
- Time-series data with composite unique keys

### Example
```csharp
// 845M row table - indexes can't fit in memory
var insertedCount = db.BulkInsertWithDeduplication(
    newRecords,
    "Timestamp", "Symbol", "ExchangeId"  // Composite key
);
// Returns: Number of new rows inserted (duplicates filtered)
```

### Breaking Changes
None - fully backward compatible

## [1.4.0] - 2025-10-03

### Added
- **High-Performance Bulk Insert** 🚀 - 10-100x faster inserts using DuckDB's native Appender API
  - New `BulkInsert<T>()` extension method for blazing-fast bulk data loading
  - `BulkInsertAsync<T>()` async variant available (pseudo-async wrapper)
  - Uses DuckDB's Appender API for direct memory-to-database transfer
  - Supports all data types: DateTime, Guid, decimal, byte[], TimeSpan, etc.
  - 10 comprehensive bulk insert tests (100% passing)
  - Performance: 1,000 rows in ~10ms, 10,000 rows in ~50ms, 100,000 rows in ~500ms

### Performance Comparison
| Method | 1,000 rows | 10,000 rows | 100,000 rows |
|--------|-----------|------------|--------------|
| `InsertAll()` | ~100ms | ~1s | ~10s |
| `BulkInsert()` | ~10ms | ~50ms | ~500ms |
| **Speedup** | **10x** | **20x** | **20-100x** |

### Important Notes
- **No transaction participation**: Appender auto-commits on completion
- **No return values**: Generated IDs are not returned (unlike `Insert()`)
- **All-or-nothing**: If any row fails, entire batch fails
- Use `InsertAll()` when you need transaction control or generated IDs

### Improved
- Test coverage increased to 100 tests (100% passing)
- Optimized concurrency tests to run 2x faster (31s total test runtime)
- Reduced test data sizes while maintaining full coverage

### Example
```csharp
using var db = dbFactory.Open();

var products = new List<Product>();
for (int i = 0; i < 100000; i++)
{
    products.Add(new Product {
        Id = i,
        Name = $"Product {i}",
        Price = i * 1.5m
    });
}

// 10-100x faster than InsertAll
db.BulkInsert(products);  // Uses DuckDB's Appender API

// Async variant
await db.BulkInsertAsync(products);
```

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
  - Cleaner syntax: `new DuckDbOrmLiteConnectionFactory<CryptoPrice>(...)`

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

## [1.0.1] - 2025-10-01

### Fixed
- **CRITICAL**: Auto-configure `BeforeExecFilter` in `DuckDbOrmLiteConnectionFactory`
  - Fixed "Values were not provided for the following prepared statement parameters" error
  - The filter is now automatically configured when creating the connection factory
  - Handles DuckDB's parameter name requirements ($ prefix, 1-based indexing)
  - Converts `DbType.Currency` to `DbType.Decimal` automatically

This was a critical bug that prevented v1.0.0 from working in real applications. The tests passed because they had a global filter setup, but applications using the library would fail on any parameterized query.

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

---

[1.3.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.3.0
[1.2.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.2.0
[1.1.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.1.0
[1.0.1]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.1
[1.0.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.0
