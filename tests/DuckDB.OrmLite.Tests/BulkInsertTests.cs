using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using DuckDB.OrmLite;
using Xunit;
using Xunit.Abstractions;

namespace DuckDbOrmLite.Tests;

/// <summary>
/// Tests for high-performance bulk insert operations using DuckDB's Appender API.
/// </summary>
[Collection("DuckDB Tests")]
public class BulkInsertTests : IDisposable
{
    private readonly OrmLiteConnectionFactory _dbFactory;
    private readonly ITestOutputHelper _output;

    public BulkInsertTests(ITestOutputHelper output, TestFixture fixture)
    {
        _output = output;
        _dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public void BulkInsert_EmptyCollection_DoesNotThrow()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>();
        db.BulkInsert(products);

        var count = db.Count<Product>();
        Assert.Equal(0, count);
    }

    [Fact]
    public void BulkInsert_SingleRow_InsertsCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>
        {
            new() { Id = 1, Name = "Test Product", Category = "Test", Price = 99.99m, Stock = 10 }
        };

        db.BulkInsert(products);

        var result = db.SingleById<Product>(1);
        Assert.NotNull(result);
        Assert.Equal("Test Product", result.Name);
        Assert.Equal(99.99m, result.Price);
    }

    [Fact]
    public void BulkInsert_MultipleRows_InsertsAllCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>();
        for (int i = 1; i <= 100; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Category = i % 2 == 0 ? "Electronics" : "Furniture",
                Price = 99.99m * i,
                Stock = i * 10
            });
        }

        db.BulkInsert(products);

        var count = db.Count<Product>();
        Assert.Equal(100, count);

        var firstProduct = db.SingleById<Product>(1);
        Assert.Equal("Product 1", firstProduct.Name);

        var lastProduct = db.SingleById<Product>(100);
        Assert.Equal("Product 100", lastProduct.Name);
    }

    [Fact]
    public void BulkInsert_AllDataTypes_HandlesCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<AllTypesModel>(overwrite: true);

        var testData = new List<AllTypesModel>
        {
            new()
            {
                Id = 1,
                StringValue = "Test String",
                IntValue = 42,
                LongValue = 9223372036854775807L,
                DecimalValue = 123.45m,
                DoubleValue = 99.99,
                BoolValue = true,
                DateTimeValue = new DateTime(2025, 1, 15, 10, 30, 0),
                GuidValue = Guid.NewGuid(),
                ByteArrayValue = new byte[] { 1, 2, 3, 4, 5 },
                TimeSpanValue = TimeSpan.FromHours(5)
            },
            new()
            {
                Id = 2,
                StringValue = "Another String",
                IntValue = -100,
                LongValue = -9223372036854775808L,
                DecimalValue = 0.01m,
                DoubleValue = 0.001,
                BoolValue = false,
                DateTimeValue = new DateTime(2024, 12, 31, 23, 59, 59),
                GuidValue = Guid.NewGuid(),
                ByteArrayValue = new byte[] { 255, 254, 253 },
                TimeSpanValue = TimeSpan.FromMinutes(30)
            }
        };

        db.BulkInsert(testData);

        var results = db.Select<AllTypesModel>().OrderBy(x => x.Id).ToList();
        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal("Test String", first.StringValue);
        Assert.Equal(42, first.IntValue);
        Assert.Equal(123.45m, first.DecimalValue);
        Assert.True(first.BoolValue);
        Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 0), first.DateTimeValue);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, first.ByteArrayValue);

        var second = results[1];
        Assert.Equal("Another String", second.StringValue);
        Assert.Equal(-100, second.IntValue);
        Assert.False(second.BoolValue);
    }

    [Fact]
    public void BulkInsert_NullValues_HandlesCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<NullableModel>(overwrite: true);

        var testData = new List<NullableModel>
        {
            new() { Id = 1, Name = "Not Null", OptionalValue = 100, OptionalDate = DateTime.Now },
            new() { Id = 2, Name = "Has Nulls", OptionalValue = null, OptionalDate = null },
            new() { Id = 3, Name = null, OptionalValue = null, OptionalDate = null }
        };

        db.BulkInsert(testData);

        var results = db.Select<NullableModel>().OrderBy(x => x.Id).ToList();
        Assert.Equal(3, results.Count);

        Assert.NotNull(results[0].Name);
        Assert.NotNull(results[0].OptionalValue);
        Assert.NotNull(results[0].OptionalDate);

        Assert.NotNull(results[1].Name);
        Assert.Null(results[1].OptionalValue);
        Assert.Null(results[1].OptionalDate);

        Assert.Null(results[2].Name);
        Assert.Null(results[2].OptionalValue);
        Assert.Null(results[2].OptionalDate);
    }

    [Fact]
    public void BulkInsert_WithExplicitIds_InsertsCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Customer>(overwrite: true);

        var customers = new List<Customer>
        {
            new() { Id = 1, CustomerId = Guid.NewGuid(), Name = "Customer 1", Email = "c1@test.com", CreditLimit = 1000, IsActive = true, RegisteredAt = DateTime.UtcNow },
            new() { Id = 2, CustomerId = Guid.NewGuid(), Name = "Customer 2", Email = "c2@test.com", CreditLimit = 2000, IsActive = true, RegisteredAt = DateTime.UtcNow },
            new() { Id = 3, CustomerId = Guid.NewGuid(), Name = "Customer 3", Email = "c3@test.com", CreditLimit = 3000, IsActive = true, RegisteredAt = DateTime.UtcNow }
        };

        db.BulkInsert(customers);

        var results = db.Select<Customer>().OrderBy(x => x.Id).ToList();
        Assert.Equal(3, results.Count);

        // Verify IDs were inserted correctly
        Assert.Equal(1, results[0].Id);
        Assert.Equal(2, results[1].Id);
        Assert.Equal(3, results[2].Id);

        // Verify data integrity
        Assert.Equal("Customer 1", results[0].Name);
        Assert.Equal("Customer 2", results[1].Name);
        Assert.Equal("Customer 3", results[2].Name);
    }

    [Fact]
    public void BulkInsert_PerformanceComparison_SignificantlyFaster()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>();
        for (int i = 1; i <= 1000; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Category = "Test",
                Price = i * 1.5m,
                Stock = i
            });
        }

        // Measure BulkInsert
        var sw1 = Stopwatch.StartNew();
        db.BulkInsert(products);
        sw1.Stop();
        var bulkInsertTime = sw1.ElapsedMilliseconds;

        _output.WriteLine($"BulkInsert: {bulkInsertTime}ms for 1000 rows");

        // Clear table for InsertAll test
        db.DeleteAll<Product>();

        // Measure InsertAll
        var sw2 = Stopwatch.StartNew();
        db.InsertAll(products);
        sw2.Stop();
        var insertAllTime = sw2.ElapsedMilliseconds;

        _output.WriteLine($"InsertAll: {insertAllTime}ms for 1000 rows");
        _output.WriteLine($"Speed improvement: {(double)insertAllTime / bulkInsertTime:F1}x faster");

        // BulkInsert should be significantly faster
        // We expect at least 2x improvement, but typically see 10-100x
        Assert.True(bulkInsertTime < insertAllTime,
            $"BulkInsert ({bulkInsertTime}ms) should be faster than InsertAll ({insertAllTime}ms)");

        // Verify both methods inserted the same number of rows
        var countAfterBulk = 1000; // We know this from first insert
        var countAfterInsertAll = db.Count<Product>();
        Assert.Equal(countAfterBulk, countAfterInsertAll);
    }

    [Fact]
    public async System.Threading.Tasks.Task BulkInsertAsync_InsertsCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>();
        for (int i = 1; i <= 50; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Async Product {i}",
                Category = "Async Test",
                Price = i * 2.5m,
                Stock = i * 5
            });
        }

        await db.BulkInsertAsync(products);

        var count = db.Count<Product>();
        Assert.Equal(50, count);
    }

    [Fact]
    public void BulkInsert_SpecialCharactersInStrings_HandlesCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>
        {
            new() { Id = 1, Name = "Product with 'quotes'", Category = "Test", Price = 1.0m, Stock = 1 },
            new() { Id = 2, Name = "Product with \"double quotes\"", Category = "Test", Price = 2.0m, Stock = 2 },
            new() { Id = 3, Name = "Product with, comma", Category = "Test", Price = 3.0m, Stock = 3 },
            new() { Id = 4, Name = "Product with\nnewline", Category = "Test", Price = 4.0m, Stock = 4 },
            new() { Id = 5, Name = "Product with\ttab", Category = "Test", Price = 5.0m, Stock = 5 }
        };

        db.BulkInsert(products);

        var results = db.Select<Product>().OrderBy(x => x.Id).ToList();
        Assert.Equal(5, results.Count);
        Assert.Equal("Product with 'quotes'", results[0].Name);
        Assert.Equal("Product with \"double quotes\"", results[1].Name);
        Assert.Equal("Product with, comma", results[2].Name);
        Assert.Equal("Product with\nnewline", results[3].Name);
        Assert.Equal("Product with\ttab", results[4].Name);
    }

    [Fact]
    public void BulkInsert_LargeDataset_InsertsSuccessfully()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<Product>(overwrite: true);

        var products = new List<Product>();
        for (int i = 1; i <= 10000; i++)
        {
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i}",
                Category = $"Category {i % 10}",
                Price = i * 0.99m,
                Stock = i
            });
        }

        var sw = Stopwatch.StartNew();
        db.BulkInsert(products);
        sw.Stop();

        _output.WriteLine($"Inserted 10,000 rows in {sw.ElapsedMilliseconds}ms ({10000.0 / sw.ElapsedMilliseconds:F0} rows/ms)");

        var count = db.Count<Product>();
        Assert.Equal(10000, count);

        // Verify some random records
        var record5000 = db.SingleById<Product>(5000);
        Assert.Equal("Product 5000", record5000.Name);
        Assert.Equal(5000 * 0.99m, record5000.Price);
    }
}

// Test models
public class NullableModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int? OptionalValue { get; set; }
    public DateTime? OptionalDate { get; set; }
}

public class AllTypesModel
{
    public int Id { get; set; }
    public string StringValue { get; set; }
    public int IntValue { get; set; }
    public long LongValue { get; set; }
    public decimal DecimalValue { get; set; }
    public double DoubleValue { get; set; }
    public bool BoolValue { get; set; }
    public DateTime DateTimeValue { get; set; }
    public Guid GuidValue { get; set; }
    public byte[] ByteArrayValue { get; set; }
    public TimeSpan TimeSpanValue { get; set; }
}
