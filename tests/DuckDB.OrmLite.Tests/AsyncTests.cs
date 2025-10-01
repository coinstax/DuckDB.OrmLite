using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;

namespace DuckDB.OrmLite.Tests;

/// <summary>
/// Tests to verify async/await support for DuckDB.OrmLite
/// Note: These use ServiceStack.OrmLite's built-in async support which provides pseudo-async
/// for DuckDB (wraps synchronous operations), similar to SQLite.
/// </summary>
public class AsyncTests : IDisposable
{
    private readonly IDbConnection _db;

    public class Person
    {
        [AutoIncrement]
        public int Id { get; set; }

        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Email { get; set; } = "";
    }

    public AsyncTests()
    {
        var factory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
        _db = factory.Open();
        _db.CreateTable<Person>();
    }

    public void Dispose()
    {
        _db?.Dispose();
    }

    // ==================== SELECT ASYNC ====================

    [Fact]
    public async Task Can_SelectAsync_all_records()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var results = await _db.SelectAsync<Person>();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Can_SelectAsync_with_predicate()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var results = await _db.SelectAsync<Person>(x => x.Age > 35);

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
    }

    [Fact]
    public async Task Can_SelectAsync_with_sql_expression()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var q = _db.From<Person>().Where(x => x.Name == "Alice");
        var results = await _db.SelectAsync(q);

        Assert.Single(results);
        Assert.Equal(30, results[0].Age);
    }

    // ==================== SINGLE ASYNC ====================

    [Fact]
    public async Task Can_SingleAsync_with_predicate()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var result = await _db.SingleAsync<Person>(x => x.Name == "Alice");

        Assert.NotNull(result);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task SingleAsync_returns_null_when_not_found()
    {
        var result = await _db.SingleAsync<Person>(x => x.Name == "NonExistent");

        Assert.Null(result);
    }

    // ==================== SCALAR ASYNC ====================

    [Fact]
    public async Task Can_ScalarAsync_with_sql_expression()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });
        _db.Insert(new Person { Name = "Charlie", Age = 50, Email = "charlie@test.com" });

        var maxAge = await _db.ScalarAsync<int>(_db.From<Person>().Select(x => Sql.Max(x.Age)));

        Assert.Equal(50, maxAge);
    }

    // ==================== COUNT ASYNC ====================

    [Fact]
    public async Task Can_CountAsync_all_records()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });
        _db.Insert(new Person { Name = "Charlie", Age = 50, Email = "charlie@test.com" });

        var count = await _db.CountAsync<Person>();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Can_CountAsync_with_predicate()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });
        _db.Insert(new Person { Name = "Charlie", Age = 50, Email = "charlie@test.com" });

        var count = await _db.CountAsync<Person>(x => x.Age > 35);

        Assert.Equal(2, count);
    }

    // ==================== INSERT ASYNC ====================

    [Fact]
    public async Task Can_InsertAsync()
    {
        var person = new Person { Name = "Alice", Age = 30, Email = "alice@test.com" };

        await _db.InsertAsync(person);

        var count = await _db.CountAsync<Person>();
        Assert.Equal(1, count);

        var saved = await _db.SingleAsync<Person>(x => x.Name == "Alice");
        Assert.NotNull(saved);
        Assert.Equal("Alice", saved.Name);
        Assert.Equal(30, saved.Age);
    }

    [Fact]
    public async Task Can_InsertAllAsync()
    {
        var people = new[]
        {
            new Person { Name = "Alice", Age = 30, Email = "alice@test.com" },
            new Person { Name = "Bob", Age = 40, Email = "bob@test.com" },
            new Person { Name = "Charlie", Age = 50, Email = "charlie@test.com" }
        };

        await _db.InsertAllAsync(people);

        var count = await _db.CountAsync<Person>();
        Assert.Equal(3, count);
    }

    // ==================== UPDATE ASYNC ====================

    [Fact]
    public async Task Can_UpdateAsync()
    {
        _db.Insert(new Person { Id = 1, Name = "Alice", Age = 30, Email = "alice@test.com" });

        var person = await _db.SingleAsync<Person>(x => x.Id == 1);
        person.Name = "Alice Updated";
        person.Age = 31;

        var updated = await _db.UpdateAsync(person);

        Assert.Equal(1, updated);

        var result = await _db.SingleAsync<Person>(x => x.Id == 1);
        Assert.Equal("Alice Updated", result.Name);
        Assert.Equal(31, result.Age);
    }

    [Fact]
    public async Task Can_UpdateAllAsync()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var people = await _db.SelectAsync<Person>();
        foreach (var person in people)
        {
            person.Age += 1;
        }

        var updated = await _db.UpdateAllAsync(people);

        Assert.Equal(2, updated);

        var alice = await _db.SingleAsync<Person>(x => x.Name == "Alice");
        Assert.Equal(31, alice.Age);
    }

    // ==================== DELETE ASYNC ====================

    [Fact]
    public async Task Can_DeleteAsync_with_predicate()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var deleted = await _db.DeleteAsync<Person>(x => x.Age > 35);

        Assert.Equal(1, deleted);

        var remaining = await _db.SelectAsync<Person>();
        Assert.Single(remaining);
        Assert.Equal("Alice", remaining[0].Name);
    }

    // ==================== SQL ASYNC ====================

    [Fact]
    public async Task Can_SqlListAsync()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var results = await _db.SqlListAsync<Person>("SELECT * FROM Person WHERE Age > 25");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Can_SqlScalarAsync()
    {
        _db.Insert(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
        _db.Insert(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

        var count = await _db.SqlScalarAsync<long>("SELECT COUNT(*) FROM Person");

        Assert.Equal(2, count);
    }

    // ==================== TRANSACTIONS ====================

    [Fact]
    public async Task Can_use_async_with_transactions()
    {
        using (var trans = _db.OpenTransaction())
        {
            await _db.InsertAsync(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
            await _db.InsertAsync(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

            trans.Commit();
        }

        var count = await _db.CountAsync<Person>();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Can_use_async_with_rollback()
    {
        using (var trans = _db.OpenTransaction())
        {
            await _db.InsertAsync(new Person { Name = "Alice", Age = 30, Email = "alice@test.com" });
            await _db.InsertAsync(new Person { Name = "Bob", Age = 40, Email = "bob@test.com" });

            trans.Rollback();
        }

        var count = await _db.CountAsync<Person>();
        Assert.Equal(0, count);
    }
}
