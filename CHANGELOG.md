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

### Planned for v1.1.0
- Async/Await support - Async versions of all operations
- Bulk operations optimization using DuckDB's COPY command
- Direct Parquet/CSV operations through OrmLite
- Connection pooling improvements for better concurrent reads

### Under Consideration
- Support for DuckDB-specific types (LIST, STRUCT, MAP)
- Window functions support with LINQ extensions
- DuckDB-specific aggregate functions
- Time series optimizations and ASOF joins
- Query performance profiling integration
- Schema migration tools
- Additional DuckDB configuration options

---

[1.0.1]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.1
[1.0.0]: https://github.com/coinstax/DuckDB.OrmLite/releases/tag/v1.0.0
