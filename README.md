# DuckDB.OrmLite

DuckDB provider for [ServiceStack.OrmLite](https://github.com/ServiceStack/ServiceStack.OrmLite) - A fast, simple, and typed ORM for .NET.

[![NuGet](https://img.shields.io/nuget/v/DuckDB.OrmLite.svg)](https://www.nuget.org/packages/DuckDB.OrmLite/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.md)

## Disclaimer

**This is an independent, community-maintained provider for ServiceStack.OrmLite. It is not officially maintained or endorsed by ServiceStack.**

## About

This package enables ServiceStack.OrmLite to work with [DuckDB](https://duckdb.org/), an in-process analytical database management system. DuckDB excels at:

- **Fast analytical queries** - Columnar storage and vectorized execution
- **In-process** - No separate server process required
- **SQL standard** - Familiar SQL syntax with PostgreSQL compatibility
- **Data processing** - Native support for Parquet, CSV, JSON
- **OLAP workloads** - Aggregations, window functions, complex analytics

## Features

✅ **Full OrmLite Support**
- Complete CRUD operations (sync + async)
- LINQ query expressions
- Transactions
- AutoIncrement with sequences and INSERT...RETURNING
- Complex queries (JOINs, aggregations, subqueries)
- Parameterized queries
- Batch operations
- Async/await support (pseudo-async)
- **Multi-database support** - Query across multiple DuckDB files transparently

✅ **Complete Type Support**
- All .NET primitive types
- DateTime, DateTimeOffset, TimeSpan
- Decimal and all integer types
- Guid (UUID)
- byte[] (BLOB)
- Nullable types

✅ **Production Ready**
- 75 comprehensive tests (100% passing)
- Optimized for DuckDB 1.3.2
- SQL injection prevention
- Robust error handling

## Installation

```bash
dotnet add package DuckDB.OrmLite
```

## Quick Start

```csharp
using ServiceStack.OrmLite;
using DuckDB.OrmLite;

// Create connection factory
var dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=myapp.db");

// Define your models
public class Customer
{
    [AutoIncrement]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public string Email { get; set; }

    public decimal CreditLimit { get; set; }

    public DateTime RegisteredAt { get; set; }
}

// Use OrmLite
using var db = dbFactory.Open();

// Create table
db.CreateTable<Customer>();

// Insert
var customer = new Customer
{
    Name = "Acme Corp",
    Email = "contact@acme.com",
    CreditLimit = 50000,
    RegisteredAt = DateTime.UtcNow
};
db.Insert(customer);

// Query with LINQ
var highValueCustomers = db.Select<Customer>(c =>
    c.CreditLimit > 10000 && c.RegisteredAt > DateTime.UtcNow.AddMonths(-6)
);

// Update
customer.CreditLimit = 75000;
db.Update(customer);

// Aggregations
var totalCredit = db.Scalar<decimal>(
    db.From<Customer>().Select(c => Sql.Sum(c.CreditLimit))
);

// JOINs
var orders = db.Select(db.From<Order>()
    .Join<Customer>((o, c) => o.CustomerId == c.Id)
    .Where<Customer>(c => c.Name == "Acme Corp")
);

// Transactions
using (var trans = db.OpenTransaction())
{
    db.Insert(customer);
    db.Insert(order);
    trans.Commit();
}
```

## Async/Await Support

DuckDB.OrmLite supports async/await for all operations through ServiceStack.OrmLite's built-in async APIs.

**Important**: Since DuckDB.NET.Data does not provide native async I/O operations, these are **pseudo-async** implementations (similar to SQLite). Operations will block the calling thread but provide API compatibility with other OrmLite providers.

### Async Examples

```csharp
using var db = dbFactory.Open();

// SELECT operations
var customers = await db.SelectAsync<Customer>(c => c.CreditLimit > 10000);
var customer = await db.SingleAsync<Customer>(c => c.Id == 1);
var count = await db.CountAsync<Customer>();

// INSERT operations
await db.InsertAsync(new Customer { Name = "New Corp", ... });
await db.InsertAllAsync(customers);

// UPDATE operations
customer.CreditLimit = 75000;
await db.UpdateAsync(customer);
await db.UpdateAllAsync(customers);

// DELETE operations
await db.DeleteAsync<Customer>(c => c.CreditLimit < 1000);
await db.DeleteByIdAsync<Customer>(customerId);

// SQL operations
var results = await db.SqlListAsync<Customer>("SELECT * FROM Customer WHERE CreditLimit > 50000");
var total = await db.SqlScalarAsync<decimal>("SELECT SUM(CreditLimit) FROM Customer");

// Transactions work with async too
using (var trans = db.OpenTransaction())
{
    await db.InsertAsync(customer);
    await db.InsertAsync(order);
    trans.Commit();
}
```

### Async Limitations

⚠️ **Not true async I/O**: Operations still block the calling thread internally
- Suitable for maintaining consistent async/await code style
- API-compatible with other OrmLite providers
- Not suitable for high-concurrency scenarios expecting true async I/O benefits
- Consider using synchronous methods if async benefits are not needed

## Multi-Database Support

Query across multiple DuckDB database files transparently - perfect for time-series data, archival scenarios, or partitioned datasets.

### Basic Configuration

```csharp
var factory = new DuckDbOrmLiteConnectionFactory("Data Source=main.db")
    .WithAdditionalDatabases("archive_2024.db", "archive_2023.db")
    .WithMultiDatabaseTables("CmcPrice", "FiatPrice");

// Queries automatically span all databases
using var db = factory.Open();
var allPrices = db.Select<CmcPrice>(x => x.Symbol == "BTC");
// Returns data from main.db, archive_2024.db, and archive_2023.db

// Writes go to main database only
using var writeDb = factory.OpenForWrite();
writeDb.Insert(new CmcPrice { Date = DateTime.Today, Symbol = "ETH", Price = 3500 });
```

### Type-Safe Configuration

```csharp
var factory = new DuckDbOrmLiteConnectionFactory("Data Source=main.db")
    .WithAdditionalDatabases("archive.db")
    .WithMultiDatabaseTable<CmcPrice>();  // Type-safe table configuration
```

### Use Case: Time-Series Data with Daily Updates

```csharp
// Setup: Current year + archives
var factory = new DuckDbOrmLiteConnectionFactory("Data Source=prices_2025.db")
    .WithAdditionalDatabases("prices_2024.db", "prices_2023.db", "prices_2022.db")
    .WithMultiDatabaseTables("CmcPrice");

// Read: Query spans all years transparently
using (var db = factory.Open())
{
    // Get Bitcoin prices for last 2 years
    var btcPrices = db.Select<CmcPrice>(x =>
        x.Symbol == "BTC" &&
        x.Date >= DateTime.Today.AddYears(-2));

    // Aggregations work across all databases
    var avgPrice = db.Scalar<decimal>(
        db.From<CmcPrice>()
            .Where(x => x.Symbol == "BTC")
            .Select(x => Sql.Avg(x.Price))
    );
}

// Write: New data goes to current year database
using (var writeDb = factory.OpenForWrite())
{
    writeDb.Insert(new CmcPrice
    {
        Date = DateTime.Today,
        Symbol = "BTC",
        Price = 95000
    });
}
```

### How It Works

1. **Automatic ATTACH**: Additional databases are attached on connection open
2. **Unified Views**: Creates `{TableName}_Unified` views with `UNION ALL` across all databases
3. **Query Routing**: Read queries automatically use unified views; writes go directly to main database
4. **Zero Code Changes**: Application code using `db.Select<T>()` works unchanged

### Multi-Database Features

✅ **All OrmLite operations work across databases:**
- SELECT with WHERE, ORDER BY, LIMIT
- Aggregations (COUNT, SUM, AVG, MAX, MIN)
- JOINs (multi-db table with single-db table)
- Complex LINQ expressions
- Async operations

✅ **Smart table detection:**
- Only creates views for tables that exist in databases
- Handles tables existing in subset of databases

✅ **Flexible configuration:**
- Mix multi-db and single-db tables in same factory
- Toggle auto-configuration with `.WithAutoConfigureViews(false)`

### Multi-Database Limitations

⚠️ **Important considerations:**
- **Schema consistency**: Tables must have identical schemas across all databases
- **No cross-database transactions**: Transactions only work with `OpenForWrite()` (single database)
- **Read-only archives**: Additional databases should be read-only for data consistency
- **No automatic deduplication**: `UNION ALL` doesn't deduplicate - ensure partitioning prevents duplicates

### Best Practices

**Recommended partitioning strategies:**
- Time-based: One database per year/month
- Categorical: Separate databases by data type or category
- Archival: Current database + historical archives

**Workflow pattern:**
```csharp
// Daily process: Keep current database for writes
using (var writeDb = factory.OpenForWrite())
{
    writeDb.InsertAll(todaysData);
}

// Analytics: Query across all time periods
using (var readDb = factory.Open())
{
    var historicalTrends = readDb.Select<CmcPrice>(x =>
        x.Date >= new DateTime(2020, 1, 1));
}

// Year-end: Rotate current to archive
// 1. Copy prices_2025.db to archive location
// 2. Update factory configuration to include new archive
// 3. Create fresh prices_2026.db for new year
```

## Use Cases

### Data Analysis & Reporting
```csharp
// DuckDB excels at analytical queries
var salesByMonth = db.SqlList<dynamic>(@"
    SELECT
        DATE_TRUNC('month', OrderDate) as Month,
        COUNT(*) as OrderCount,
        SUM(TotalAmount) as Revenue
    FROM Orders
    GROUP BY Month
    ORDER BY Month DESC
");
```

### ETL & Data Processing
```csharp
// Efficient bulk operations
db.InsertAll(largeDataset);

// Process with SQL
db.ExecuteSql(@"
    INSERT INTO ProcessedOrders
    SELECT * FROM Orders
    WHERE Status = 'completed'
    AND OrderDate > CURRENT_DATE - INTERVAL '30 days'
");
```

### In-Memory Analytics
```csharp
// Use in-memory database for fast processing
var dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
```

## Configuration

### Connection Strings

**File-based database:**
```csharp
"Data Source=myapp.db"
```

**In-memory database:**
```csharp
"Data Source=:memory:"
```

**Read-only mode:**
```csharp
"Data Source=myapp.db;Read Only=true"
```

### Required Setup

DuckDB requires parameter handling that differs slightly from other databases. The provider includes a `BeforeExecFilter` that handles this automatically:

```csharp
// Automatically configured by DuckDbOrmLiteConnectionFactory
// Handles:
// - Parameter name conversion ($ prefix handling)
// - 1-based positional parameter indexing
// - DbType.Currency → DbType.Decimal conversion
```

## Dependencies

This package depends on:
- [ServiceStack.OrmLite](https://www.nuget.org/packages/ServiceStack.OrmLite) (>= 8.5.2) - The ORM framework
- [DuckDB.NET.Data.Full](https://www.nuget.org/packages/DuckDB.NET.Data.Full) (>= 1.3.0) - .NET bindings for DuckDB

Both dependencies are automatically installed when you add this package.

## Compatibility

- **.NET**: 8.0+
- **DuckDB**: 1.3.2+
- **ServiceStack.OrmLite**: 8.5.2+

## Performance

DuckDB is optimized for analytical workloads:

- **Fast aggregations** - Columnar storage enables efficient aggregations
- **Vectorized execution** - SIMD optimizations for bulk operations
- **Memory efficient** - Optimized for large datasets
- **Zero-copy reads** - Direct memory access where possible

## Limitations

- **TimeSpan**: Limited to ~24 hours when using HH:MM:SS format
- **Concurrent writes**: DuckDB uses single-writer model

## Documentation

- [DuckDB Official Documentation](https://duckdb.org/docs/)
- [ServiceStack.OrmLite Documentation](https://docs.servicestack.net/ormlite/)
- [Development Documentation](docs/) - Implementation details and history

## Testing

```bash
# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "FullyQualifiedName~AdvancedFeatureTests"
```

Test coverage:
- 25 core OrmLite functionality tests
- 15 advanced feature tests (JOINs, aggregations, edge cases)
- Production-ready error handling and SQL injection prevention

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Acknowledgments

- [ServiceStack.OrmLite](https://github.com/ServiceStack/ServiceStack.OrmLite) - The excellent ORM framework
- [DuckDB](https://duckdb.org/) - The fast in-process analytical database
- [DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) - .NET bindings for DuckDB

## Support

- **Issues**: [GitHub Issues](https://github.com/coinstax/DuckDB.OrmLite/issues)
- **ServiceStack OrmLite**: [ServiceStack Support](https://servicestack.net/support)
- **DuckDB**: [DuckDB Discord](https://discord.duckdb.org/)
