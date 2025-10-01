# DuckDB.OrmLite Development Roadmap

## Current Status: v1.0.1 (Production Ready)

- ✅ Complete CRUD operations
- ✅ LINQ query support
- ✅ All .NET data types supported
- ✅ 40 tests (39 passing - 97.5%)
- ✅ Published to NuGet
- ✅ Critical bug fix (BeforeExecFilter auto-configuration)

## Upcoming Releases

### v1.1.0 - Async/Await Support

**Target**: Q1 2025

**Goal**: Add comprehensive async/await API support for modern .NET applications

**Features**:
- Async versions of all CRUD operations
- API-compatible with ServiceStack.OrmLite async patterns
- CancellationToken support throughout
- Pseudo-async implementation (wraps sync operations)

**Key Methods**:
- `SelectAsync`, `SingleAsync`, `ScalarAsync`, `CountAsync`, `ExistsAsync`
- `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `SaveAsync`
- `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `SqlListAsync`

**Documentation**: See [ASYNC_SUPPORT_SPEC.md](ASYNC_SUPPORT_SPEC.md)

**Estimated Effort**: 10-14 hours

**Important Note**: This is pseudo-async (not true async I/O) since DuckDB.NET.Data v1.3.0 doesn't support native async operations. However, it provides API compatibility and allows consistent async/await code style.

---

### v1.2.0 - Multi-Database Support

**Target**: Q2 2025

**Goal**: Enable transparent querying across multiple DuckDB database files

**Use Cases**:
- Time-series data partitioned by year/month
- Staging databases for gap-filling workflows
- Archive databases for historical data
- Zero downtime updates

**Features**:
- Fluent configuration API for multi-database setup
- Automatic VIEW creation for unified queries
- Read/write separation (reads span all DBs, writes to main DB)
- Complete transparency - application code unchanged

**Example Configuration**:
```csharp
var factory = new DuckDbOrmLiteConnectionFactory("cmcprice.db")
    .WithAdditionalDatabases("cmcprice_staging.db", "cmcprice_2024.db")
    .WithMultiDatabaseTables("CmcPrice", "FiatPrice");

// Application code unchanged
using (var db = factory.Open())
{
    var prices = db.Select<CmcPrice>(x => x.Symbol == "BTC");
    // Automatically queries across all databases
}
```

**Documentation**: See [MULTI_DATABASE_SPEC.md](MULTI_DATABASE_SPEC.md)

**Estimated Effort**: 16-24 hours

---

### v1.3.0 - Performance & Bulk Operations

**Target**: Q3 2025

**Goal**: Optimize for large-scale data operations

**Features**:
- Bulk operations using DuckDB's COPY command
- Direct Parquet/CSV file querying through OrmLite
- Optimized batch insert/update operations
- Connection pooling improvements

**Performance Targets**:
- 10x faster bulk inserts (COPY vs INSERT)
- Direct Parquet querying without import
- Better handling of DuckDB's single-writer model

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

### v1.0.1 (2025-10-01) - Critical Fix
**Status**: ✅ Released

- Fixed BeforeExecFilter not being auto-configured
- Resolved "Values were not provided for parameters" error
- Made library usable in real applications without manual setup

**Impact**: Critical - v1.0.0 was broken for real-world usage

### v1.0.0 (2025-10-01) - Initial Release
**Status**: ✅ Released

- Complete OrmLite implementation
- Full CRUD operations
- LINQ query expressions
- Transaction support
- All .NET primitive types supported
- 40 comprehensive tests
- NuGet package published

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

### For v1.1.0 (Async Support)
1. Read [ASYNC_SUPPORT_SPEC.md](ASYNC_SUPPORT_SPEC.md)
2. Implement async extensions in `src/DuckDB.OrmLite/DuckDbAsyncExtensions.cs`
3. Add tests in `tests/DuckDB.OrmLite.Tests/AsyncTests.cs`
4. Update README.md with async examples and limitations

### For v1.2.0 (Multi-Database)
1. Read [MULTI_DATABASE_SPEC.md](MULTI_DATABASE_SPEC.md)
2. Extend `DuckDbOrmLiteConnectionFactory` with configuration API
3. Modify `DuckDbDialectProvider` for view redirection
4. Add tests for multi-database scenarios
5. Create migration guide for existing applications

### General Guidelines
1. All new features must have specifications in `docs/`
2. Update CHANGELOG.md with feature descriptions
3. Maintain test coverage above 95%
4. Follow existing code style and patterns
5. Document limitations and trade-offs clearly

---

## Questions or Feedback?

- **GitHub Issues**: https://github.com/coinstax/DuckDB.OrmLite/issues
- **Discussions**: https://github.com/coinstax/DuckDB.OrmLite/discussions

---

**Last Updated**: 2025-10-01
