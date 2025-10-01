# DuckDB OrmLite Provider for ServiceStack

A custom OrmLite dialect provider for using DuckDB with ServiceStack 8.5.2.

## Features

- **DuckDB-specific parameter syntax**: Uses `$1`, `$2` positional parameters instead of `@param`
- **Complete type mapping**: Supports all DuckDB data types including UUID, HUGEINT, temporal types
- **Proper type conversions**: Handles .NET to DuckDB type conversions automatically
- **Standard OrmLite API**: Works with all ServiceStack OrmLite features

## Installation

1. Install required NuGet packages:
```bash
dotnet add package ServiceStack.OrmLite
dotnet add package DuckDB.NET.Data
```

2. Copy the following files to your project:
   - `DuckDbDialectProvider.cs`
   - `DuckDbTypeConverters.cs`
   - `DuckDbOrmLiteConnectionFactory.cs`

## Usage

### Basic Setup

```csharp
using ServiceStack.OrmLite.DuckDb;

// Create connection factory
var dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=mydb.duckdb");

// Or use the extension method
var dbFactory = DuckDbOrmLiteConnectionFactoryExtensions
    .CreateDuckDbConnectionFactory("Data Source=mydb.duckdb");

// Open connection
using var db = dbFactory.Open();
```

### In-Memory Database

```csharp
var dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
using var db = dbFactory.Open();
```

### Create Tables

```csharp
public class User
{
    [AutoIncrement]
    public int Id { get; set; }

    public Guid UserId { get; set; }  // Maps to UUID
    public string Name { get; set; }  // Maps to VARCHAR
    public DateTime CreatedAt { get; set; }  // Maps to TIMESTAMP
    public decimal Balance { get; set; }  // Maps to DECIMAL
}

db.CreateTable<User>();
```

### CRUD Operations

```csharp
// Insert
db.Insert(new User
{
    UserId = Guid.NewGuid(),
    Name = "John Doe",
    CreatedAt = DateTime.UtcNow,
    Balance = 100.50m
});

// Select
var users = db.Select<User>(x => x.Balance > 50);

// Update
db.Update(new User { Id = 1, Name = "Jane Doe" });

// Delete
db.Delete<User>(x => x.Id == 1);
```

### Parameterized Queries

The provider automatically handles DuckDB's `$1`, `$2` parameter syntax:

```csharp
// This works - parameters are automatically converted to $1, $2, etc.
var users = db.Select<User>("WHERE Name = $1 AND Balance > $2", "John", 100);
```

## Type Mappings

| .NET Type | DuckDB Type |
|-----------|-------------|
| `bool` | `BOOLEAN` |
| `sbyte` | `TINYINT` |
| `byte` | `UTINYINT` |
| `short` | `SMALLINT` |
| `ushort` | `USMALLINT` |
| `int` | `INTEGER` |
| `uint` | `UINTEGER` |
| `long` | `BIGINT` |
| `ulong` | `UBIGINT` |
| `BigInteger` | `HUGEINT` |
| `float` | `REAL` |
| `double` | `DOUBLE` |
| `decimal` | `DECIMAL(18,6)` |
| `Guid` | `UUID` |
| `string` | `VARCHAR` |
| `byte[]` | `BLOB` |
| `DateTime` | `TIMESTAMP` |
| `DateTimeOffset` | `TIMESTAMPTZ` |
| `TimeSpan` | `INTERVAL` |

## Key Differences from Other Providers

1. **Parameters**: DuckDB uses `$1`, `$2` instead of `@param` (SQL Server) or `:param` (SQLite)
2. **Auto-increment**: Uses `INTEGER PRIMARY KEY` instead of `IDENTITY` or `AUTOINCREMENT`
3. **UUID support**: Native `UUID` type instead of storing as string or binary
4. **Type strictness**: DuckDB requires explicit type conversions in some cases

## Limitations

- Foreign key constraint support is basic
- Some advanced DuckDB features (arrays, structs) may require custom converters
- Transaction isolation levels may differ from other databases

## License

Same as ServiceStack OrmLite
