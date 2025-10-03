# DuckDB.OrmLite Development Roadmap

## Current Status: v1.4.0 (Production Ready)

- ✅ Complete CRUD operations (sync + async)
- ✅ LINQ query support
- ✅ All .NET data types supported
- ✅ **High-performance bulk insert** - 10-100x faster using Appender API
- ✅ Multi-database support - Query across multiple DuckDB files
- ✅ Connection timeout/retry - Exponential backoff for multi-process scenarios
- ✅ Generic factory - Type-safe configuration
- ✅ 100 tests (100% passing)
- ✅ Published to NuGet

## Upcoming Releases

### v1.5.0 - Direct Parquet/CSV Operations

**Target**: Q1 2026

**Goal**: Query external data files directly without importing

**Features**:
- Direct Parquet file querying through OrmLite
- Direct CSV file querying through OrmLite
- Read external files as if they were tables
- Zero-copy access to columnar formats

**Example Usage**:
```csharp
// Query Parquet file directly
var results = db.Select<LogEntry>("parquet_scan('logs/*.parquet')");

// Query CSV file directly
var data = db.Select<SalesData>("read_csv_auto('sales.csv')");
```

**Performance Targets**:
- No import overhead - query files directly
- Leverages DuckDB's native file format support
- Ideal for data lake scenarios

**Estimated Effort**: 12-16 hours

---

### v1.6.0 - DuckDB Complex Types

**Target**: Q2 2026

**Goal**: Native support for DuckDB-specific data types

**Features**:
- Native support for LIST types
- Native support for STRUCT types
- Native support for MAP types
- Complex nested data structures

**Example Usage**:
```csharp
public class EventLog
{
    public int Id { get; set; }
    public List<string> Tags { get; set; }  // DuckDB LIST
    public Dictionary<string, object> Metadata { get; set; }  // DuckDB MAP
}
```

**Estimated Effort**: 20-24 hours

---

### v1.7.0 - Advanced Query Features

**Target**: Q3 2026

**Goal**: DuckDB-specific analytical functions

**Features**:
- Window functions with LINQ extensions
- DuckDB-specific aggregates (APPROX_COUNT_DISTINCT, etc.)
- Time series optimizations
- ASOF joins for time-series data

**Example Usage**:
```csharp
// Window functions
var ranked = db.Select<Product>(db.From<Product>()
    .SelectDistinct(x => new {
        x.Name,
        Rank = Sql.Custom("ROW_NUMBER() OVER (PARTITION BY Category ORDER BY Price DESC)")
    }));

// ASOF joins for time-series
var prices = db.AsOfJoin<Price, Trade>((p, t) => p.Timestamp <= t.Timestamp);
```

**Estimated Effort**: 24-30 hours

---

## Under Consideration (Future Versions)

### DuckDB-Specific Features
- Native support for DuckDB types (LIST, STRUCT, MAP)
- Window functions with LINQ extensions
- DuckDB-specific aggregate functions
- Time series optimizations and ASOF joins

### Developer Experience
- Query performance profiling integration (EXPLAIN ANALYZE)
- Schema migration tools
- Additional DuckDB configuration options
- Query plan visualization

### Advanced Features
- Streaming result sets for large queries
- Read-only connection optimization
- WAL mode configuration helpers
- Memory management utilities

---

## Completed Milestones

### v1.4.0 (2025-10-03) - High-Performance Bulk Insert
**Status**: ✅ Released

- **BulkInsert API** - 10-100x faster than InsertAll using DuckDB's Appender API
- `BulkInsert<T>()` and `BulkInsertAsync<T>()` extension methods
- Direct memory-to-database transfer for maximum performance
- 10 comprehensive bulk insert tests
- Optimized concurrency tests for 2x faster test suite
- 100 tests total (100% passing)

**Performance**:
- 1,000 rows: ~10ms (10x faster)
- 10,000 rows: ~50ms (20x faster)
- 100,000 rows: ~500ms (20-100x faster)

**Impact**: Major performance improvement for bulk data loading scenarios

---

### v1.3.0 (2025-10-01) - Connection Management & Generic Factory
**Status**: ✅ Released

- **Connection Timeout/Retry** - Exponential backoff for multi-process lock conflicts
- `Open(TimeSpan timeout)` and `OpenForWrite(TimeSpan timeout)` methods
- Configurable retry delays: `RetryDelayMs` and `MaxRetryDelayMs` properties
- **Generic Factory** - Type-safe multi-database configuration
- `DuckDbOrmLiteConnectionFactory<T>` for cleaner syntax
- **11 concurrency tests** validating MVCC behavior
- **CRITICAL FIX**: BeforeExecFilter now isolated to DuckDB connections only
- 90 tests (100% passing)

**Impact**: Better multi-process support and type-safe configuration

---

### v1.2.0 (2025-10-01) - Multi-Database Support
**Status**: ✅ Released

- **Multi-Database Queries** - Transparent querying across multiple DuckDB files
- Fluent API: `.WithAdditionalDatabases()` and `.WithMultiDatabaseTables()`
- Automatic VIEW creation with UNION ALL
- Read/write separation: `Open()` for multi-db, `OpenForWrite()` for single-db
- Smart table detection - handles tables in subset of databases
- 18 comprehensive multi-database tests
- 75 tests total (100% passing)

**Use Cases**: Time-series partitioning, archival scenarios, data lake queries

**Impact**: Enable scalable data partitioning strategies

---

### v1.1.0 (2025-10-01) - Async/Await Support
**Status**: ✅ Released

- **Async API** - Complete async/await support for all operations
- `SelectAsync`, `InsertAsync`, `UpdateAsync`, `DeleteAsync`, etc.
- API-compatible with ServiceStack.OrmLite async patterns
- 17 comprehensive async tests
- Pseudo-async implementation (wraps sync - DuckDB.NET limitation)
- 57 tests total (100% passing)

**Impact**: Modern async/await code style support

---

### v1.0.1 (2025-10-01) - Critical Fix
**Status**: ✅ Released

- Fixed BeforeExecFilter not being auto-configured
- Resolved "Values were not provided for parameters" error
- Made library usable in real applications without manual setup

**Impact**: Critical - v1.0.0 was broken for real-world usage

---

### v1.0.0 (2025-10-01) - Initial Release
**Status**: ✅ Released

- Complete OrmLite implementation
- Full CRUD operations
- LINQ query expressions
- Transaction support
- All .NET primitive types supported
- 40 comprehensive tests
- NuGet package published

**Impact**: First production-ready release

---

## Development Principles

1. **Backward Compatibility** - No breaking changes in minor versions
2. **Zero Code Changes** - New features should be opt-in and transparent
3. **API Consistency** - Follow ServiceStack.OrmLite conventions exactly
4. **Documentation First** - Specs before implementation
5. **Test Coverage** - Comprehensive tests for all features
6. **Production Ready** - All releases must be stable and well-tested

---

## How to Contribute

### For v1.5.0 (Direct Parquet/CSV)
1. Create specification document: `EXTERNAL_FILES_SPEC.md`
2. Research DuckDB's `parquet_scan()` and `read_csv_auto()` functions
3. Implement extension methods for external file queries
4. Add comprehensive tests for various file formats
5. Update README.md with examples and use cases

### For v1.6.0 (Complex Types)
1. Create specification document: `COMPLEX_TYPES_SPEC.md`
2. Design type converters for LIST, STRUCT, MAP
3. Implement serialization/deserialization logic
4. Add tests for nested data structures
5. Document type mapping and limitations

### General Guidelines
1. All new features must have specifications in `docs/`
2. Update CHANGELOG.md with feature descriptions
3. Maintain test coverage above 95%
4. Follow existing code style and patterns
5. Document limitations and trade-offs clearly
6. Ensure backward compatibility (no breaking changes in minor versions)

---

## Questions or Feedback?

- **GitHub Issues**: https://github.com/coinstax/DuckDB.OrmLite/issues
- **Discussions**: https://github.com/coinstax/DuckDB.OrmLite/discussions

---

**Last Updated**: 2025-10-03

---

## Version History Summary

| Version | Release Date | Key Feature | Status |
|---------|-------------|-------------|--------|
| v1.4.0 | 2025-10-03 | High-Performance Bulk Insert | ✅ Released |
| v1.3.0 | 2025-10-01 | Connection Timeout/Retry + Generic Factory | ✅ Released |
| v1.2.0 | 2025-10-01 | Multi-Database Support | ✅ Released |
| v1.1.0 | 2025-10-01 | Async/Await Support | ✅ Released |
| v1.0.1 | 2025-10-01 | Critical BeforeExecFilter Fix | ✅ Released |
| v1.0.0 | 2025-10-01 | Initial Release | ✅ Released |
| v1.5.0 | Q1 2026 | Direct Parquet/CSV Operations | 📋 Planned |
| v1.6.0 | Q2 2026 | DuckDB Complex Types | 📋 Planned |
| v1.7.0 | Q3 2026 | Advanced Query Features | 📋 Planned |
