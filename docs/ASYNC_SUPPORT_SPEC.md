# Async/Await Support Specification

## Overview

Add comprehensive async/await support to DuckDB.OrmLite, following ServiceStack.OrmLite's async API patterns. This enables non-blocking database operations for improved scalability in high-concurrency applications.

## Current State

### ServiceStack.OrmLite Async Support
- ✅ Comprehensive async API with `*Async` suffix for all operations
- ✅ `CancellationToken` support for all async methods
- ✅ Works with SQL Server, PostgreSQL, MySQL (true async)
- ✅ Works with SQLite (pseudo-async - wraps sync methods)

### DuckDB.NET.Data Status
- ❌ **No native async support** in DuckDB.NET.Data v1.3.0
- ❌ No `ExecuteReaderAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync`
- ⚠️ Similar to SQLite - we must implement pseudo-async (wrap sync methods)

## Implementation Strategy

Since DuckDB.NET.Data doesn't support true async operations, we'll implement **pseudo-async** like SQLite's OrmLite provider:
- Wrap synchronous operations with `Task.FromResult()` / `Task.Run()`
- Accept `CancellationToken` but only check before operations
- Maintain API compatibility with other OrmLite providers
- Document that operations are not truly async

## API Surface

### Read Operations

#### SelectAsync
```csharp
// Lambda expressions
Task<List<T>> SelectAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken token = default);

// SqlExpression
Task<List<T>> SelectAsync<T>(SqlExpression<T> expression, CancellationToken token = default);
Task<List<T>> SelectAsync<T>(Func<SqlExpression<T>, SqlExpression<T>> expression, CancellationToken token = default);

// Raw SQL
Task<List<T>> SelectAsync<T>(string sql, CancellationToken token = default);
Task<List<T>> SelectAsync<T>(string sql, object anonType, CancellationToken token = default);
Task<List<T>> SelectAsync<T>(string sql, Dictionary<string, object> dict, CancellationToken token = default);
```

#### SingleAsync
```csharp
Task<T> SingleAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken token = default);
Task<T> SingleAsync<T>(SqlExpression<T> expression, CancellationToken token = default);
Task<T> SingleByIdAsync<T>(object idValue, CancellationToken token = default);
```

#### ScalarAsync
```csharp
Task<T> ScalarAsync<T>(SqlExpression<T> expression, CancellationToken token = default);
Task<T> ScalarAsync<T, TKey>(Expression<Func<T, TKey>> field, CancellationToken token = default);
Task<long> ScalarAsync<T>(Expression<Func<T, object>> expression, CancellationToken token = default);
```

#### CountAsync
```csharp
Task<long> CountAsync<T>(CancellationToken token = default);
Task<long> CountAsync<T>(Expression<Func<T, bool>> expression, CancellationToken token = default);
Task<long> CountAsync<T>(SqlExpression<T> expression, CancellationToken token = default);
```

#### ExistsAsync
```csharp
Task<bool> ExistsAsync<T>(Expression<Func<T, bool>> expression, CancellationToken token = default);
Task<bool> ExistsAsync<T>(SqlExpression<T> expression, CancellationToken token = default);
```

### Write Operations

#### InsertAsync
```csharp
Task<long> InsertAsync<T>(T obj, bool selectIdentity = false, CancellationToken token = default);
Task InsertAllAsync<T>(IEnumerable<T> objs, CancellationToken token = default);
Task InsertOnlyAsync<T>(T obj, Func<T, object> onlyFields, CancellationToken token = default);
```

#### UpdateAsync
```csharp
Task<int> UpdateAsync<T>(T obj, CancellationToken token = default);
Task<int> UpdateAllAsync<T>(IEnumerable<T> objs, CancellationToken token = default);
Task<int> UpdateAsync<T>(T obj, Expression<Func<T, bool>> where, CancellationToken token = default);
Task<int> UpdateOnlyAsync<T>(T obj, Func<T, object> onlyFields, CancellationToken token = default);
Task<int> UpdateOnlyAsync<T>(T obj, Expression<Func<T, object>> onlyFields, Expression<Func<T, bool>> where, CancellationToken token = default);
```

#### DeleteAsync
```csharp
Task<int> DeleteAsync<T>(T obj, CancellationToken token = default);
Task<int> DeleteAsync<T>(Expression<Func<T, bool>> where, CancellationToken token = default);
Task<int> DeleteByIdAsync<T>(object id, CancellationToken token = default);
Task<int> DeleteByIdsAsync<T>(IEnumerable<object> ids, CancellationToken token = default);
Task<int> DeleteAllAsync<T>(CancellationToken token = default);
```

#### SaveAsync
```csharp
Task<bool> SaveAsync<T>(T obj, CancellationToken token = default);
Task SaveAllAsync<T>(IEnumerable<T> objs, CancellationToken token = default);
```

### Execute Operations

```csharp
Task<int> ExecuteNonQueryAsync(string sql, CancellationToken token = default);
Task<int> ExecuteNonQueryAsync(string sql, object anonType, CancellationToken token = default);

Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken token = default);
Task<T> ExecuteScalarAsync<T>(string sql, object anonType, CancellationToken token = default);

Task<List<T>> SqlListAsync<T>(string sql, CancellationToken token = default);
Task<List<T>> SqlListAsync<T>(string sql, object anonType, CancellationToken token = default);
Task<List<T>> SqlListAsync<T>(string sql, Dictionary<string, object> dict, CancellationToken token = default);

Task<T> SqlScalarAsync<T>(string sql, CancellationToken token = default);
Task<T> SqlScalarAsync<T>(string sql, object anonType, CancellationToken token = default);
```

## Implementation Approach

### Pattern: Pseudo-Async Wrapper

Since DuckDB.NET.Data is synchronous, we'll wrap operations similar to SQLite:

```csharp
// Example implementation pattern
public static async Task<List<T>> SelectAsync<T>(
    this IDbConnection dbConn,
    Expression<Func<T, bool>> predicate,
    CancellationToken token = default)
{
    token.ThrowIfCancellationRequested();

    // Wrap sync operation
    return await Task.Run(() => dbConn.Select(predicate), token).ConfigAwait();
}

// Alternative for simple operations
public static Task<long> CountAsync<T>(
    this IDbConnection dbConn,
    CancellationToken token = default)
{
    token.ThrowIfCancellationRequested();

    // For quick operations, avoid Task.Run overhead
    var result = dbConn.Count<T>();
    return Task.FromResult(result);
}
```

### Decision: Task.Run vs Task.FromResult

**Use `Task.Run`** for:
- Long-running queries (SELECT with large result sets)
- Bulk operations (InsertAll, UpdateAll, DeleteAll)
- Complex queries with JOINs/aggregations

**Use `Task.FromResult`** for:
- Quick operations (Count, Exists, SingleById)
- DDL operations (CreateTable, DropTable)
- Small single-row operations

**Rationale**:
- DuckDB queries are typically fast (analytical workload)
- `Task.Run` adds thread pool overhead
- Most operations will complete quickly enough to use `Task.FromResult`
- For truly long operations, users can wrap in their own `Task.Run`

### Where to Implement

Create new file: `src/DuckDB.OrmLite/DuckDbAsyncExtensions.cs`

```csharp
namespace ServiceStack.OrmLite;

public static class DuckDbReadApiAsync
{
    // SELECT operations
    public static Task<List<T>> SelectAsync<T>(
        this IDbConnection dbConn,
        Expression<Func<T, bool>> predicate,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = dbConn.Select(predicate);
        return Task.FromResult(result);
    }

    // ... all other read operations
}

public static class DuckDbWriteApiAsync
{
    // INSERT operations
    public static Task<long> InsertAsync<T>(
        this IDbConnection dbConn,
        T obj,
        bool selectIdentity = false,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = dbConn.Insert(obj, selectIdentity);
        return Task.FromResult(result);
    }

    // ... all other write operations
}

public static class DuckDbExecuteSqlAsync
{
    // Raw SQL operations
    public static Task<int> ExecuteNonQueryAsync(
        this IDbConnection dbConn,
        string sql,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var result = dbConn.ExecuteNonQuery(sql);
        return Task.FromResult(result);
    }

    // ... all other execute operations
}
```

## Testing Requirements

### Unit Tests

Create: `tests/DuckDB.OrmLite.Tests/AsyncTests.cs`

```csharp
public class AsyncTests : IDisposable
{
    private readonly IDbConnection _db;

    public AsyncTests()
    {
        var factory = new DuckDbOrmLiteConnectionFactory(":memory:");
        _db = factory.Open();
        _db.CreateTable<Person>();
    }

    [Fact]
    public async Task Can_SelectAsync_with_predicate()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });
        await _db.InsertAsync(new Person { Id = 2, Name = "Bob", Age = 40 });

        var results = await _db.SelectAsync<Person>(x => x.Age > 35);

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
    }

    [Fact]
    public async Task Can_InsertAsync_and_return_identity()
    {
        var person = new Person { Name = "Charlie", Age = 25 };

        var id = await _db.InsertAsync(person, selectIdentity: true);

        Assert.True(id > 0);
    }

    [Fact]
    public async Task Can_UpdateAsync()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });

        var updated = await _db.UpdateAsync(new Person { Id = 1, Name = "Alice Updated", Age = 31 });

        Assert.Equal(1, updated);
        var person = await _db.SingleByIdAsync<Person>(1);
        Assert.Equal("Alice Updated", person.Name);
    }

    [Fact]
    public async Task Can_DeleteAsync()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });

        var deleted = await _db.DeleteAsync<Person>(x => x.Id == 1);

        Assert.Equal(1, deleted);
        var count = await _db.CountAsync<Person>();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Can_CountAsync()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });
        await _db.InsertAsync(new Person { Id = 2, Name = "Bob", Age = 40 });

        var count = await _db.CountAsync<Person>(x => x.Age > 35);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Can_ExistsAsync()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });

        var exists = await _db.ExistsAsync<Person>(x => x.Name == "Alice");
        var notExists = await _db.ExistsAsync<Person>(x => x.Name == "Charlie");

        Assert.True(exists);
        Assert.False(notExists);
    }

    [Fact]
    public async Task Can_ScalarAsync()
    {
        await _db.InsertAsync(new Person { Id = 1, Name = "Alice", Age = 30 });
        await _db.InsertAsync(new Person { Id = 2, Name = "Bob", Age = 40 });

        var maxAge = await _db.ScalarAsync<Person, int>(x => Sql.Max(x.Age));

        Assert.Equal(40, maxAge);
    }

    [Fact]
    public async Task CancellationToken_throws_when_cancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _db.SelectAsync<Person>(x => x.Age > 0, cts.Token));
    }

    [Fact]
    public async Task Can_InsertAllAsync()
    {
        var people = new[]
        {
            new Person { Id = 1, Name = "Alice", Age = 30 },
            new Person { Id = 2, Name = "Bob", Age = 40 },
            new Person { Id = 3, Name = "Charlie", Age = 50 }
        };

        await _db.InsertAllAsync(people);

        var count = await _db.CountAsync<Person>();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Can_SaveAsync()
    {
        // Insert
        var person = new Person { Id = 1, Name = "Alice", Age = 30 };
        await _db.SaveAsync(person);

        var saved = await _db.SingleByIdAsync<Person>(1);
        Assert.Equal("Alice", saved.Name);

        // Update
        person.Name = "Alice Updated";
        await _db.SaveAsync(person);

        var updated = await _db.SingleByIdAsync<Person>(1);
        Assert.Equal("Alice Updated", updated.Name);
    }

    public void Dispose()
    {
        _db?.Dispose();
    }
}
```

### Integration Tests

Add async versions to existing test files:
- `ExampleUsageTests.cs` → Add async examples
- `AdvancedFeatureTests.cs` → Add async complex queries
- `DuckDbOrmLiteTests.cs` → Add async CRUD tests

## Performance Considerations

### Pseudo-Async Overhead

Since we're implementing pseudo-async:
- **No thread pool release** - operations block the calling thread
- **Not suitable for** - ASP.NET Core high-concurrency scenarios expecting true async I/O
- **Suitable for** - Maintaining API compatibility, async/await code style

### Documentation Notes

We must document:
```markdown
## Async Support

DuckDB.OrmLite supports async/await API methods for API compatibility with other
OrmLite providers. However, since DuckDB.NET.Data does not provide native async
operations, these are **pseudo-async** wrappers around synchronous operations.

**What this means:**
- ✅ You can use `await` syntax throughout your codebase
- ✅ API-compatible with other OrmLite providers
- ✅ `CancellationToken` support for operation cancellation
- ⚠️ Operations still block the calling thread (not true async I/O)
- ⚠️ No performance benefit over synchronous operations

**When to use:**
- Maintaining consistent async/await code style
- Code that targets multiple OrmLite providers
- Applications with low concurrency requirements

**When NOT to use:**
- High-concurrency web APIs expecting async I/O benefits
- When synchronous operations would be clearer
```

## Migration Path

### For Existing Code

No changes required - sync methods remain:
```csharp
// Existing sync code continues to work
var people = db.Select<Person>(x => x.Age > 30);
```

### Adding Async Support

Simple transformation:
```csharp
// Before
var people = db.Select<Person>(x => x.Age > 30);
db.Insert(newPerson);

// After
var people = await db.SelectAsync<Person>(x => x.Age > 30);
await db.InsertAsync(newPerson);
```

## Future Considerations

### If DuckDB.NET.Data Adds True Async

When/if DuckDB.NET.Data implements true async operations:
1. Update implementation to use native async methods
2. No API changes required - consumers already use async APIs
3. Performance improvements without code changes
4. Update documentation to reflect true async support

## Release Plan

- **Target Version**: v1.1.0
- **Dependencies**: None (uses existing DuckDB.NET.Data v1.3.0)
- **Breaking Changes**: None (additive only)

## Estimated Effort

- **Implementation**: 8-12 hours
  - Read operations: 3-4 hours
  - Write operations: 3-4 hours
  - Execute operations: 1-2 hours
  - Testing: 2-3 hours
- **Documentation**: 2 hours
- **Total**: ~10-14 hours

## Success Criteria

- ✅ All major async operations implemented
- ✅ API-compatible with ServiceStack.OrmLite async APIs
- ✅ Comprehensive test coverage (20+ async tests)
- ✅ CancellationToken support working correctly
- ✅ Documentation clearly explains pseudo-async nature
- ✅ No breaking changes to existing sync code
- ✅ All existing tests still pass

---

**Status**: Specification complete - Ready for implementation
