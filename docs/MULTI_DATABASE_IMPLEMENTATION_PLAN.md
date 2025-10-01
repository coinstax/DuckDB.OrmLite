# Multi-Database Support - Implementation Plan

## Overview

Implement transparent querying across multiple DuckDB database files by extending `DuckDbOrmLiteConnectionFactory` and `DuckDbDialectProvider`. Application code remains unchanged - multi-database support is enabled via fluent configuration.

## Implementation Phases

### Phase 1: Core Infrastructure (Foundation)

**Goal**: Add multi-database configuration and connection management

#### 1.1 Extend DuckDbOrmLiteConnectionFactory

**File**: `src/DuckDB.OrmLite/DuckDbOrmLiteConnectionFactory.cs`

**Changes**:
```csharp
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    // New fields for multi-database support
    private string[] _additionalDatabases;
    private HashSet<string> _multiDbTables;
    private bool _autoConfigureViews = true;

    // Fluent configuration methods
    public DuckDbOrmLiteConnectionFactory WithAdditionalDatabases(params string[] databases)
    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTables(params string[] tableNames)
    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTable<T>(params string[] databases)
    public DuckDbOrmLiteConnectionFactory WithAutoConfigureViews(bool enable)

    // Override Open() to configure multi-database
    public override IDbConnection Open()

    // New method for write-only connections
    public IDbConnection OpenForWrite()

    // Internal configuration methods
    private void ConfigureMultiDatabase(IDbConnection conn)
    private void AttachDatabases(IDbConnection conn)
    private void CreateUnifiedViews(IDbConnection conn)
    private string GetDatabaseAlias(string dbPath)
}
```

**Details**:
- Store configuration for additional databases and multi-db tables
- Override `Open()` to attach databases and create views automatically
- Add `OpenForWrite()` that bypasses view creation (direct to main DB)
- Sanitize database paths to create valid SQL aliases

**Test Coverage**:
- ✅ Configuration methods set properties correctly
- ✅ Single database mode (no additional databases)
- ✅ Open() vs OpenForWrite() behavior difference
- ✅ Database alias sanitization (handle `-`, `.`, spaces)

#### 1.2 Enhance DuckDbDialectProvider

**File**: `src/DuckDB.OrmLite/DuckDbDialectProvider.cs`

**Changes**:
```csharp
public class DuckDbDialectProvider : OrmLiteDialectProviderBase<DuckDbDialectProvider>
{
    // New fields
    private HashSet<string> _multiDbTables;

    // Configuration method (called by factory)
    internal void SetMultiDatabaseTables(HashSet<string> multiDbTables)

    // Override to redirect to unified views
    public override string GetQuotedTableName(ModelDefinition modelDef)
}
```

**Details**:
- Add internal configuration method for factory to set multi-db tables
- Override `GetQuotedTableName()` to check if table is multi-db
- If multi-db, return `"{TableName}_Unified"` instead of `"{TableName}"`
- Maintain backward compatibility for single-db mode

**Test Coverage**:
- ✅ GetQuotedTableName returns correct name for single-db tables
- ✅ GetQuotedTableName returns unified view name for multi-db tables
- ✅ Null/empty multi-db tables configuration

### Phase 2: Database Attachment & View Creation

**Goal**: Implement ATTACH DATABASE and CREATE VIEW logic

#### 2.1 Database Attachment

**Method**: `AttachDatabases(IDbConnection conn)`

**Logic**:
```csharp
private void AttachDatabases(IDbConnection conn)
{
    if (_additionalDatabases == null || _additionalDatabases.Length == 0)
        return;

    foreach (var dbPath in _additionalDatabases)
    {
        // Validate path exists
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"Database file not found: {dbPath}");

        var alias = GetDatabaseAlias(dbPath);
        var sql = $"ATTACH DATABASE IF NOT EXISTS '{dbPath}' AS {alias}";
        conn.ExecuteSql(sql);
    }
}

private string GetDatabaseAlias(string dbPath)
{
    // Extract filename without extension
    var filename = Path.GetFileNameWithoutExtension(dbPath);

    // Sanitize to valid SQL identifier
    return filename
        .Replace("-", "_")
        .Replace(".", "_")
        .Replace(" ", "_");
}
```

**Error Handling**:
- ❌ Database file not found → `FileNotFoundException`
- ❌ Invalid database file → `DuckDBException` (propagate)
- ❌ Database already attached with different alias → Ignore (ATTACH IF NOT EXISTS)

**Test Coverage**:
- ✅ Attach single additional database
- ✅ Attach multiple databases (2, 3, 5)
- ✅ Handle non-existent database file
- ✅ Alias sanitization (various filenames)
- ✅ Idempotent (can call multiple times)

#### 2.2 Unified View Creation

**Method**: `CreateUnifiedViews(IDbConnection conn)`

**Logic**:
```csharp
private void CreateUnifiedViews(IDbConnection conn)
{
    if (_multiDbTables == null || _multiDbTables.Count == 0)
        return;

    foreach (var tableName in _multiDbTables)
    {
        CreateUnifiedView(conn, tableName);
    }
}

private void CreateUnifiedView(IDbConnection conn, string tableName)
{
    // Build UNION ALL query
    var unionClauses = new List<string>();

    // Main database
    unionClauses.Add($"SELECT * FROM main.\"{tableName}\"");

    // Additional databases
    foreach (var dbPath in _additionalDatabases)
    {
        var alias = GetDatabaseAlias(dbPath);

        // Check if table exists in this database
        if (TableExistsInDatabase(conn, alias, tableName))
        {
            unionClauses.Add($"SELECT * FROM {alias}.\"{tableName}\"");
        }
    }

    // Create or replace the view
    var viewName = $"{tableName}_Unified";
    var sql = $@"
        CREATE OR REPLACE VIEW ""{viewName}"" AS
        {string.Join("\nUNION ALL\n", unionClauses)}
    ";

    conn.ExecuteSql(sql);
}

private bool TableExistsInDatabase(IDbConnection conn, string dbAlias, string tableName)
{
    var sql = $@"
        SELECT COUNT(*)
        FROM {dbAlias}.information_schema.tables
        WHERE table_name = $1
    ";

    var count = conn.SqlScalar<long>(sql, tableName);
    return count > 0;
}
```

**Error Handling**:
- ❌ Table doesn't exist in ANY database → Log warning, skip
- ❌ Schema mismatch across databases → DuckDB will error on UNION, propagate
- ✅ Table exists in some but not all databases → Only union existing tables

**Test Coverage**:
- ✅ Create view for table in all databases
- ✅ Create view when table exists in main + some (not all) additional databases
- ✅ Skip view creation if table doesn't exist in any database
- ✅ View can be queried successfully
- ✅ CREATE OR REPLACE (idempotent)

### Phase 3: Query Routing & Testing

**Goal**: Ensure queries use correct tables/views

#### 3.1 Read Query Routing

**Implementation**: Already handled by `GetQuotedTableName()` override

**Test Cases**:
```csharp
// Test: SELECT queries use unified view
var factory = new DuckDbOrmLiteConnectionFactory("main.db")
    .WithAdditionalDatabases("archive_2024.db", "archive_2023.db")
    .WithMultiDatabaseTables("CmcPrice");

using (var db = factory.Open())
{
    // Should query CmcPrice_Unified view
    var prices = db.Select<CmcPrice>(x => x.Symbol == "BTC");

    // Verify data from all databases
    Assert.True(prices.Any(p => p.Date.Year == 2025)); // from main.db
    Assert.True(prices.Any(p => p.Date.Year == 2024)); // from archive_2024.db
}
```

**Test Coverage**:
- ✅ SELECT with WHERE clause
- ✅ SELECT with complex predicate (LINQ)
- ✅ COUNT, SUM, AVG aggregations
- ✅ ORDER BY, LIMIT
- ✅ JOINs (multi-db table with single-db table)
- ✅ Subqueries

#### 3.2 Write Query Routing

**Implementation**: `OpenForWrite()` bypasses view creation

**Test Cases**:
```csharp
// Test: INSERT goes to main database only
using (var db = factory.OpenForWrite())
{
    db.Insert(new CmcPrice { Date = DateTime.Today, Symbol = "ETH", ... });

    // Verify data only in main.db
    var mainConn = new DuckDBConnection("main.db");
    var count = mainConn.SqlScalar<long>("SELECT COUNT(*) FROM CmcPrice WHERE Symbol = 'ETH'");
    Assert.Equal(1, count);
}

// Test: UPDATE/DELETE only affect main database
using (var db = factory.OpenForWrite())
{
    db.Update(new CmcPrice { Id = 1, Symbol = "BTC", ... });
    db.Delete<CmcPrice>(x => x.Symbol == "OLD");
}
```

**Test Coverage**:
- ✅ INSERT single record
- ✅ InsertAll bulk insert
- ✅ UPDATE by ID
- ✅ UPDATE with predicate
- ✅ DELETE by ID
- ✅ DELETE with predicate
- ✅ Transactions with OpenForWrite()

### Phase 4: Edge Cases & Error Handling

**Goal**: Handle edge cases gracefully

#### 4.1 Schema Validation (Optional - Future)

**Future Enhancement**: Add method to validate schemas match across databases

```csharp
public bool ValidateSchemas(string tableName)
{
    // Query information_schema from each database
    // Compare column names, types, nullability
    // Return true if all match, false otherwise
}
```

**For v1.2.0**: Document requirement, don't implement validation

#### 4.2 Duplicate Data Handling

**Approach**: Application responsibility to avoid duplicates

**Documentation**:
- Clearly state that UNION ALL does not deduplicate
- Recommend partitioning strategies (e.g., by date range)
- Document that duplicates are expected if data overlaps

#### 4.3 Connection Lifecycle

**Considerations**:
- Views are session-specific in DuckDB → Must recreate on each connection
- ATTACH is session-specific → Must re-attach on each connection
- Factory's `Open()` handles this automatically

**Test Coverage**:
- ✅ Multiple sequential connections
- ✅ Concurrent connections (each has own views)
- ✅ Connection pooling behavior

### Phase 5: Documentation & Examples

**Goal**: Comprehensive documentation for users

#### 5.1 README.md Updates

**Section**: "Multi-Database Support"

**Content**:
- Overview of feature
- Use cases (time-series, partitioned data)
- Configuration examples
- Read vs write pattern
- Limitations and best practices

#### 5.2 CHANGELOG.md Updates

**Section**: v1.2.0

**Content**:
- Added multi-database support
- Configuration API
- Read/write separation
- Test coverage

#### 5.3 Code Examples

Create `examples/MultiDatabaseExample.cs`:
- Basic setup
- Daily update pattern
- Gap filling pattern
- Archive rotation

## Implementation Order

### Sprint 1: Foundation (Week 1)
1. Extend `DuckDbOrmLiteConnectionFactory` with configuration methods
2. Add multi-database fields and fluent API
3. Implement `OpenForWrite()` method
4. Write unit tests for configuration

### Sprint 2: Core Logic (Week 1)
5. Implement `AttachDatabases()` method
6. Implement `CreateUnifiedViews()` method
7. Extend `DuckDbDialectProvider.GetQuotedTableName()`
8. Write integration tests for attachment and views

### Sprint 3: Query Routing (Week 2)
9. Test read queries across multiple databases
10. Test write queries to main database only
11. Test aggregations, JOINs, subqueries
12. Write performance benchmarks

### Sprint 4: Polish & Documentation (Week 2)
13. Handle edge cases and errors
14. Write comprehensive documentation
15. Create example code
16. Final testing and bug fixes

## Test Strategy

### Unit Tests (~15 tests)
- Configuration API correctness
- Database alias sanitization
- GetQuotedTableName logic
- TableExistsInDatabase helper

### Integration Tests (~25 tests)
- Single database backward compatibility
- Multi-database attachment
- Unified view creation
- Read query routing
- Write query routing
- Aggregations across databases
- JOINs with multi-db tables
- Transactions
- Concurrent connections
- Error cases (missing file, schema mismatch)

### Performance Tests (~5 tests)
- Query performance: 1 vs 2 vs 5 databases
- View creation overhead
- Connection open latency
- Memory usage with multiple databases
- Large dataset queries (millions of rows)

**Total**: ~45 new tests (target: 100% passing)

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|----------|
| Schema mismatch across databases | High | Document requirement; future: add validation |
| Duplicate data in results | Medium | Document as expected behavior; recommend partitioning |
| Performance degradation | Medium | Benchmark and optimize; DuckDB's optimizer handles UNION ALL well |
| Connection overhead | Low | Views are lightweight; ATTACH is fast |
| Breaking changes | High | Ensure backward compatibility with single-db mode |

## Success Criteria

1. ✅ All existing 57 tests pass (backward compatibility)
2. ✅ 45 new multi-database tests pass (100%)
3. ✅ Zero breaking changes to public API
4. ✅ Performance: <10% overhead for single-db, <20% for 5 databases
5. ✅ Documentation complete with examples
6. ✅ User can migrate from single-db to multi-db with <10 lines of code change

## Questions & Decisions

### Resolved
- **Q**: Should we implement `OpenForRead()` + `OpenForWrite()` or just override `Open()`?
  - **A**: Override `Open()` for multi-db, add `OpenForWrite()` for single-db writes

- **Q**: Should views be created eagerly (on Open) or lazily (on first query)?
  - **A**: Eager (on Open) - simpler, fail-fast, minor overhead

- **Q**: Should we validate schema consistency?
  - **A**: Not in v1.2.0 - document requirement, add in future version

### Open Questions
1. Should we support wildcard database patterns (`cmcprice_*.db`)? → v1.3.0
2. Should we add duplicate detection/DISTINCT option? → Based on user feedback
3. Should we expose per-query database hints? → v1.3.0 if needed
4. Should we add automatic archive rotation? → v1.3.0

## Release Checklist

- [ ] All tests passing (102 total: 57 existing + 45 new)
- [ ] Documentation updated (README, CHANGELOG, examples)
- [ ] Performance benchmarks run and documented
- [ ] Breaking change analysis complete (none expected)
- [ ] Code review by project maintainer
- [ ] NuGet package version bumped to 1.2.0
- [ ] Git tag created: v1.2.0
- [ ] GitHub release created with release notes
- [ ] Published to NuGet
- [ ] Announcement/blog post (optional)

---

**Status**: Ready for implementation
**Target Release**: v1.2.0
**Estimated Effort**: 2-3 weeks
**Priority**: High (core feature for production use case)
