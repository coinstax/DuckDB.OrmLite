# Multi-Database Support Specification

## Overview

Extend `DuckDbDialectProvider` and `DuckDbOrmLiteConnectionFactory` to support transparent querying across multiple DuckDB database files. This enables read-heavy workloads to scale by partitioning data across multiple files while maintaining a simple, unified API for application code.

## Goals

1. **Transparent API** - Application code uses standard OrmLite syntax without awareness of multiple databases
2. **Zero Code Changes** - Existing code using `db.Select<T>()` continues to work unchanged
3. **Flexible Configuration** - Easy to configure which tables span multiple databases
4. **Read/Write Separation** - Clear separation between read queries (multi-db) and writes (single db)
5. **Backward Compatible** - Single-database usage remains unchanged

## Use Cases

### Primary Use Case: Time-Series Data with Updates
- **Daily Updates**: Append new daily data to current year database
- **Gap Filling**: Insert missing historical data discovered during queries
- **Read-Heavy**: Most queries are read-only across all time periods
- **Minimal Downtime**: Updates should not block read queries

### Example Scenario
```
data/
├── cmcprice.db              # Current year (read-write)
├── cmcprice_staging.db      # Temporary updates/gap fills
├── cmcprice_2024.db         # Archive (read-only)
├── cmcprice_2023.db         # Archive (read-only)
└── cmcprice_2022.db         # Archive (read-only)
```

## Technical Design

### 1. Configuration API

```csharp
// Single database (current behavior - unchanged)
var factory = new DuckDbOrmLiteConnectionFactory("cmcprice.db");

// Multi-database configuration (new)
var factory = new DuckDbOrmLiteConnectionFactory("cmcprice.db")
    .WithAdditionalDatabases("cmcprice_staging.db", "cmcprice_2024.db")
    .WithMultiDatabaseTables("CmcPrice", "FiatPrice");

// Alternative: Configure specific tables with their databases
var factory = new DuckDbOrmLiteConnectionFactory("cmcprice.db")
    .WithMultiDatabaseTable<CmcPrice>("cmcprice_staging.db", "cmcprice_2024.db")
    .WithMultiDatabaseTable<FiatPrice>("fiatprice_archive.db");
```

### 2. DuckDbOrmLiteConnectionFactory Changes

Add configuration options:
```csharp
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    private string[] _additionalDatabases;
    private HashSet<string> _multiDbTables;

    public DuckDbOrmLiteConnectionFactory WithAdditionalDatabases(params string[] databases)
    {
        _additionalDatabases = databases;
        return this;
    }

    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTables(params string[] tableNames)
    {
        _multiDbTables = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        return this;
    }

    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTable<T>(params string[] databases)
    {
        // Type-safe configuration
        var tableName = typeof(T).GetModelMetadata().ModelName;
        // ... configuration logic
        return this;
    }
}
```

### 3. Connection Filter (Auto-Setup)

Automatically configure multi-database support on connection open:
```csharp
private void ConfigureMultiDatabase(IDbConnection conn)
{
    if (_additionalDatabases == null || _additionalDatabases.Length == 0)
        return; // Single database mode

    // Step 1: Attach additional databases
    foreach (var dbPath in _additionalDatabases)
    {
        var alias = GetDatabaseAlias(dbPath);
        conn.ExecuteSql($"ATTACH DATABASE IF NOT EXISTS '{dbPath}' AS {alias}");
    }

    // Step 2: Create unified views for multi-db tables
    foreach (var tableName in _multiDbTables)
    {
        CreateUnifiedView(conn, tableName);
    }
}

private void CreateUnifiedView(IDbConnection conn, string tableName)
{
    var unionClauses = new List<string> { $"SELECT * FROM main.\"{tableName}\"" };

    foreach (var dbPath in _additionalDatabases)
    {
        var alias = GetDatabaseAlias(dbPath);
        unionClauses.Add($"SELECT * FROM {alias}.\"{tableName}\"");
    }

    var createViewSql = $@"
        CREATE OR REPLACE VIEW ""{tableName}_Unified"" AS
        {string.Join("\nUNION ALL\n", unionClauses)}
    ";

    conn.ExecuteSql(createViewSql);
}

private string GetDatabaseAlias(string dbPath)
{
    return Path.GetFileNameWithoutExtension(dbPath)
        .Replace("-", "_")
        .Replace(".", "_");
}
```

### 4. DuckDbDialectProvider Changes

Override table name resolution to use unified views:
```csharp
public class DuckDbDialectProvider : SqliteOrmLiteDialectProvider
{
    private HashSet<string> _multiDbTables;

    internal void SetMultiDatabaseTables(HashSet<string> multiDbTables)
    {
        _multiDbTables = multiDbTables;
    }

    public override string GetQuotedTableName(ModelDefinition modelDef, string alias = null)
    {
        // If configured for multi-database, redirect to unified view
        if (_multiDbTables != null &&
            _multiDbTables.Contains(modelDef.ModelName))
        {
            var viewName = $"{modelDef.ModelName}_Unified";
            return alias != null
                ? $"\"{viewName}\" {alias}"
                : $"\"{viewName}\"";
        }

        return base.GetQuotedTableName(modelDef, alias);
    }
}
```

### 5. Read vs Write Separation

```csharp
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    public IDbConnection Open()
    {
        var conn = base.Open();
        ConfigureMultiDatabase(conn); // Enable multi-db views
        return conn;
    }

    public IDbConnection OpenForWrite()
    {
        // Returns connection to main database only (no views)
        return base.Open();
    }
}
```

## Usage Examples

### Example 1: Basic Multi-Database Query
```csharp
// Configuration (startup)
var factory = new DuckDbOrmLiteConnectionFactory("cmcprice.db")
    .WithAdditionalDatabases("cmcprice_staging.db", "cmcprice_2024.db")
    .WithMultiDatabaseTables("CmcPrice");

// Application code (unchanged!)
using (var db = factory.Open())
{
    // Automatically queries across all databases
    var prices = db.Select<CmcPrice>(x => x.Symbol == "BTC");

    var recentPrices = db.Select<CmcPrice>(x =>
        x.Symbol == "ETH" &&
        x.Date >= DateTime.Today.AddDays(-30));
}
```

### Example 2: Write to Main Database
```csharp
// Writes go to main database only
using (var db = factory.OpenForWrite())
{
    db.Insert(new CmcPrice
    {
        Date = DateTime.Today,
        Symbol = "BTC",
        // ...
    });
}
```

### Example 3: Gap Filling Pattern
```csharp
// Read from all databases to find gaps
using (var db = factory.Open())
{
    var existingDates = db.Select<CmcPrice>(x =>
        x.Symbol == "BTC" &&
        x.Date >= new DateTime(2024, 1, 1))
        .Select(x => x.Date)
        .ToHashSet();

    // Identify missing dates
    var allDates = GetExpectedDateRange();
    var missingDates = allDates.Except(existingDates).ToList();
}

// Fetch missing data from API
var missingPrices = await FetchMissingPrices(missingDates);

// Write to staging database
var stagingFactory = new DuckDbOrmLiteConnectionFactory("cmcprice_staging.db");
using (var db = stagingFactory.Open())
{
    db.InsertAll(missingPrices);
}

// Future reads automatically include staging data
using (var db = factory.Open())
{
    var prices = db.Select<CmcPrice>(x => x.Symbol == "BTC");
    // Now includes data from staging
}
```

### Example 4: Daily Merge Process
```csharp
// Nightly process: Merge staging into main
using (var mainDb = new DuckDbOrmLiteConnectionFactory("cmcprice.db").Open())
using (var stagingDb = new DuckDbOrmLiteConnectionFactory("cmcprice_staging.db").Open())
{
    // Copy from staging to main
    var stagingData = stagingDb.Select<CmcPrice>();
    mainDb.InsertAll(stagingData);

    // Clear staging
    stagingDb.DeleteAll<CmcPrice>();
}
```

## Implementation Considerations

### 1. View Performance
- **Concern**: UNION ALL views may have overhead
- **Mitigation**: DuckDB's query optimizer handles UNION ALL efficiently; predicate pushdown ensures only relevant files are scanned
- **Testing**: Benchmark queries with 1, 2, 5, 10 databases

### 2. Schema Consistency
- **Concern**: Tables across databases must have identical schemas
- **Mitigation**: Document requirement; consider schema validation on startup
- **Future**: Add optional schema validation method

### 3. Transaction Handling
- **Concern**: Transactions can't span multiple databases in DuckDB
- **Mitigation**: Transactions only work with `OpenForWrite()` (single database)
- **Documentation**: Clearly document transaction limitations

### 4. Duplicate Data
- **Concern**: Same record might exist in multiple databases
- **Mitigation**: Application responsibility to avoid duplicates; consider adding `DISTINCT` option
- **Future**: Optional duplicate detection/resolution

### 5. Database Aliases
- **Concern**: Database filenames may not be valid SQL identifiers
- **Mitigation**: Sanitize filenames (replace `-`, `.` with `_`)

## Testing Requirements

### Unit Tests
1. Single database mode (backward compatibility)
2. Multi-database configuration
3. View creation with 2, 3, 5 databases
4. Query routing (reads use views, writes use main)
5. Database alias sanitization

### Integration Tests
1. Query across multiple databases with WHERE clauses
2. Aggregations (COUNT, SUM, AVG) across databases
3. JOINs with multi-database tables
4. Insert/Update/Delete to main database only
5. Transaction behavior with `OpenForWrite()`

### Performance Tests
1. Query performance: 1 vs 2 vs 5 databases
2. View creation overhead on connection open
3. Memory usage with large number of databases

## Documentation Requirements

1. **README.md** - Add multi-database section with examples
2. **CHANGELOG.md** - Document new feature
3. **API Documentation** - Document all new methods
4. **Migration Guide** - How to convert single-db to multi-db setup
5. **Best Practices** - When to use multi-db vs single-db

## Backward Compatibility

- ✅ Existing single-database code works unchanged
- ✅ No breaking changes to API
- ✅ New features are opt-in via fluent configuration
- ✅ Default behavior remains single-database

## Future Enhancements (Beyond Initial Implementation)

1. **Schema Validation** - Verify schemas match across databases
2. **Automatic Partitioning** - Auto-route writes based on date/key
3. **Dynamic Database Discovery** - Auto-detect databases matching pattern
4. **Conflict Resolution** - Handle duplicate keys across databases
5. **Query Hints** - Allow specifying which databases to query
6. **Statistics** - Track which databases are being queried

## Release Plan

- **Target Version**: v1.2.0 (after async support in v1.1.0)
- **Dependencies**: None (pure DuckDB features)
- **Breaking Changes**: None

## Questions to Resolve

1. Should `Open()` always use multi-db views, or add `OpenForRead()` + `OpenForWrite()`?
2. Should we validate schema consistency on startup or lazily?
3. Should we support wildcard database patterns (e.g., `cmcprice_*.db`)?
4. Should we expose a way to query specific databases only?
5. Should we add automatic duplicate detection/handling?

---

**Status**: Specification draft - Ready for review and implementation planning
