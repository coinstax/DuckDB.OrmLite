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
/// Tests for BulkInsertWithDeduplication using staging table pattern.
/// This is the recommended approach for large tables where indexes cannot fit in memory.
/// </summary>
[Collection("DuckDB Tests")]
public class BulkInsertDeduplicationTests : IDisposable
{
    private readonly OrmLiteConnectionFactory _dbFactory;
    private readonly ITestOutputHelper _output;

    public BulkInsertDeduplicationTests(ITestOutputHelper output, TestFixture fixture)
    {
        _output = output;
        _dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public void BulkInsertWithDeduplication_ExplicitUniqueColumns_FiltersDuplicates()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        var initialData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 },
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 101 },
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Symbol = "BTC", Value = 102 }
        };
        db.InsertAll(initialData);

        // Try to insert with some duplicates
        var newData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 999 }, // Duplicate
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Symbol = "BTC", Value = 999 }, // Duplicate
            new() { Timestamp = new DateTime(2025, 1, 1, 13, 0, 0), Symbol = "BTC", Value = 103 }, // New
            new() { Timestamp = new DateTime(2025, 1, 1, 14, 0, 0), Symbol = "BTC", Value = 104 }  // New
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "Symbol");

        Assert.Equal(2, insertedCount); // Only 2 new records inserted

        var allRecords = db.Select<TimeSeriesData>().OrderBy(x => x.Timestamp).ToList();
        Assert.Equal(5, allRecords.Count);

        // Verify duplicates were NOT overwritten
        var record11am = allRecords.First(r => r.Timestamp.Hour == 11);
        Assert.Equal(101, record11am.Value); // Original value, not 999
    }

    [Fact]
    public void BulkInsertWithDeduplication_AllDuplicates_InsertsZeroRows()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        var initialData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 },
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 101 }
        };
        db.InsertAll(initialData);

        // Try to insert all duplicates
        var duplicateData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 999 },
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 999 }
        };

        var insertedCount = db.BulkInsertWithDeduplication(duplicateData, "Timestamp", "Symbol");

        Assert.Equal(0, insertedCount);
        Assert.Equal(2, db.Count<TimeSeriesData>()); // Still only 2 records
    }

    [Fact]
    public void BulkInsertWithDeduplication_NoDuplicates_InsertsAllRows()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        var initialData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 }
        };
        db.InsertAll(initialData);

        // Insert all new data
        var newData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 101 },
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Symbol = "BTC", Value = 102 },
            new() { Timestamp = new DateTime(2025, 1, 1, 13, 0, 0), Symbol = "BTC", Value = 103 }
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "Symbol");

        Assert.Equal(3, insertedCount);
        Assert.Equal(4, db.Count<TimeSeriesData>());
    }

    [Fact]
    public void BulkInsertWithDeduplication_EmptyCollection_ReturnsZero()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        var insertedCount = db.BulkInsertWithDeduplication(
            new List<TimeSeriesData>(),
            "Timestamp", "Symbol");

        Assert.Equal(0, insertedCount);
    }

    [Fact]
    public void BulkInsertWithDeduplication_NoUniqueColumns_ThrowsException()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        var data = new List<TimeSeriesData>
        {
            new() { Timestamp = DateTime.Now, Symbol = "BTC", Value = 100 }
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            db.BulkInsertWithDeduplication(data, new string[0]));

        Assert.Contains("At least one unique key column must be specified", ex.Message);
    }

    [Fact]
    public void BulkInsertWithDeduplication_AutoDetectUniqueFromAttribute_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<UniqueAttributeModel>(overwrite: true);

        // Insert initial data
        var initialData = new List<UniqueAttributeModel>
        {
            new() { Email = "user1@test.com", Name = "User 1" },
            new() { Email = "user2@test.com", Name = "User 2" }
        };
        db.InsertAll(initialData);

        // Try to insert with duplicate email
        var newData = new List<UniqueAttributeModel>
        {
            new() { Email = "user2@test.com", Name = "Duplicate User" }, // Duplicate
            new() { Email = "user3@test.com", Name = "User 3" } // New
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData);

        Assert.Equal(1, insertedCount); // Only user3 inserted
        Assert.Equal(3, db.Count<UniqueAttributeModel>());

        // Verify duplicate was not overwritten
        var user2 = db.Single<UniqueAttributeModel>(x => x.Email == "user2@test.com");
        Assert.Equal("User 2", user2.Name); // Original name preserved
    }

    [Fact]
    public void BulkInsertWithDeduplication_AutoDetectCompositeIndex_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<CompositeIndexModel>(overwrite: true);

        // Insert initial data
        var initialData = new List<CompositeIndexModel>
        {
            new() { Date = new DateTime(2025, 1, 1), Symbol = "BTC", Exchange = "NYSE", Price = 100 },
            new() { Date = new DateTime(2025, 1, 1), Symbol = "ETH", Exchange = "NYSE", Price = 200 }
        };
        db.InsertAll(initialData);

        // Try to insert with composite key duplicate
        var newData = new List<CompositeIndexModel>
        {
            new() { Date = new DateTime(2025, 1, 1), Symbol = "BTC", Exchange = "NYSE", Price = 999 }, // Duplicate
            new() { Date = new DateTime(2025, 1, 2), Symbol = "BTC", Exchange = "NYSE", Price = 101 }  // New (different date)
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData);

        Assert.Equal(1, insertedCount); // Only new date inserted
        Assert.Equal(3, db.Count<CompositeIndexModel>());

        // Verify duplicate was not overwritten
        var btcJan1 = db.Single<CompositeIndexModel>(x =>
            x.Date == new DateTime(2025, 1, 1) && x.Symbol == "BTC" && x.Exchange == "NYSE");
        Assert.Equal(100, btcJan1.Price); // Original price preserved
    }

    [Fact]
    public void BulkInsertWithDeduplication_AutoDetectNoUniqueColumns_ThrowsException()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true); // No unique attributes

        var data = new List<TimeSeriesData>
        {
            new() { Timestamp = DateTime.Now, Symbol = "BTC", Value = 100 }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            db.BulkInsertWithDeduplication(data));

        Assert.Contains("No unique columns found", ex.Message);
    }

    [Fact]
    public void BulkInsertWithDeduplication_MultipleUniqueColumns_ThrowsException()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<MultipleUniqueModel>(overwrite: true);

        var data = new List<MultipleUniqueModel>
        {
            new() { Email = "test@test.com", Username = "testuser", Name = "Test" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            db.BulkInsertWithDeduplication(data));

        Assert.Contains("multiple", ex.Message);
        Assert.Contains("Email", ex.Message);
        Assert.Contains("Username", ex.Message);
    }

    [Fact]
    public void BulkInsertWithDeduplication_MultipleCompositeIndexes_ThrowsException()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<MultipleCompositeIndexModel>(overwrite: true);

        var data = new List<MultipleCompositeIndexModel>
        {
            new() { Col1 = "A", Col2 = "B", Col3 = "C" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            db.BulkInsertWithDeduplication(data));

        Assert.Contains("multiple [CompositeIndex(Unique=true)]", ex.Message);
    }

    [Fact]
    public void BulkInsertWithDeduplication_CompositeKeyWithUniqueColumn_UsesCompositeKey()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<CompositeKeyWithUniqueModel>(overwrite: true);

        // Insert initial data
        var initialData = new List<CompositeKeyWithUniqueModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", TransactionId = "TX001", Value = 100 }
        };
        db.InsertAll(initialData);

        // Try to insert:
        // - Same (Timestamp, Symbol) but different TransactionId -> Should be rejected (composite key duplicate)
        // - Different (Timestamp, Symbol) with different TransactionId -> Should be INSERTED
        var newData = new List<CompositeKeyWithUniqueModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", TransactionId = "TX002", Value = 999 }, // Duplicate composite key (rejected)
            new() { Timestamp = new DateTime(2025, 1, 2), Symbol = "BTC", TransactionId = "TX003", Value = 200 }  // New composite key (inserted)
        };

        // Auto-detect should use CompositeKey (Timestamp, Symbol), not the [Unique] TransactionId
        var insertedCount = db.BulkInsertWithDeduplication(newData);

        Assert.Equal(1, insertedCount); // Only second record inserted (different composite key)
        Assert.Equal(2, db.Count<CompositeKeyWithUniqueModel>());

        // Verify the composite key duplicate was rejected
        var results = db.Select<CompositeKeyWithUniqueModel>().OrderBy(x => x.Timestamp).ToList();
        Assert.Equal(100, results.First(r => r.Timestamp == new DateTime(2025, 1, 1)).Value); // Original preserved
        Assert.Equal("TX001", results.First(r => r.Timestamp == new DateTime(2025, 1, 1)).TransactionId); // Original TransactionId
    }

    [Fact]
    public void BulkInsertWithDeduplication_LargeDataset_PerformanceTest()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert 10,000 initial records
        var initialData = new List<TimeSeriesData>();
        for (int i = 0; i < 10000; i++)
        {
            initialData.Add(new TimeSeriesData
            {
                Timestamp = new DateTime(2025, 1, 1).AddMinutes(i),
                Symbol = "BTC",
                Value = i
            });
        }
        db.InsertAll(initialData);

        // Try to insert 1,000 new records (with 50% duplicates)
        var newData = new List<TimeSeriesData>();
        for (int i = 9500; i < 10500; i++) // 500 duplicates, 500 new
        {
            newData.Add(new TimeSeriesData
            {
                Timestamp = new DateTime(2025, 1, 1).AddMinutes(i),
                Symbol = "BTC",
                Value = i + 10000
            });
        }

        var sw = Stopwatch.StartNew();
        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "Symbol");
        sw.Stop();

        _output.WriteLine($"BulkInsertWithDeduplication: {sw.ElapsedMilliseconds}ms for 1,000 records (500 duplicates)");
        _output.WriteLine($"Inserted {insertedCount} new records");

        Assert.Equal(500, insertedCount); // Only new records inserted
        Assert.Equal(10500, db.Count<TimeSeriesData>()); // 10,000 + 500 new

        // Verify performance is reasonable (should be < 500ms for this size)
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"Performance too slow: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async System.Threading.Tasks.Task BulkInsertWithDeduplicationAsync_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        var initialData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 }
        };
        db.InsertAll(initialData);

        // Insert with async
        var newData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 999 }, // Duplicate
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 101 }  // New
        };

        var insertedCount = await db.BulkInsertWithDeduplicationAsync(newData, "Timestamp", "Symbol");

        Assert.Equal(1, insertedCount);
        Assert.Equal(2, db.Count<TimeSeriesData>());
    }

    [Fact]
    public void BulkInsertWithDeduplication_StagingTableCleanup_AlwaysExecutes()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        var data = new List<TimeSeriesData>
        {
            new() { Timestamp = DateTime.Now, Symbol = "BTC", Value = 100 }
        };

        db.BulkInsertWithDeduplication(data, "Timestamp", "Symbol");

        // Verify no staging tables left behind
        var tables = db.SqlList<string>("SELECT table_name FROM information_schema.tables WHERE table_name LIKE '%Staging%'");
        Assert.Empty(tables);
    }

    [Fact]
    public void BulkInsertWithDeduplication_ThreeColumnCompositeKey_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TripleKeyModel>(overwrite: true);

        // Insert initial data
        var initialData = new List<TripleKeyModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 1, Value = 100 },
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 2, Value = 200 }
        };
        db.InsertAll(initialData);

        // Try to insert with triple-key duplicate
        var newData = new List<TripleKeyModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 1, Value = 999 }, // Duplicate
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 3, Value = 300 }, // New (different BigintCol)
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "B", BigintCol = 1, Value = 400 }  // New (different VarcharCol)
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "VarcharCol", "BigintCol");

        Assert.Equal(2, insertedCount); // Only 2 new records
        Assert.Equal(4, db.Count<TripleKeyModel>());

        // Verify duplicate was not overwritten
        var original = db.Single<TripleKeyModel>(x =>
            x.Timestamp == new DateTime(2025, 1, 1) && x.VarcharCol == "A" && x.BigintCol == 1);
        Assert.Equal(100, original.Value); // Original value preserved
    }

    [Fact]
    public void BulkInsertWithDeduplication_845MillionRowScenario_SimulatedTest()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<LargeScaleModel>(overwrite: true);

        // Simulate the scenario: Large existing table (we'll use 1000 rows as proxy)
        var existingData = new List<LargeScaleModel>();
        for (int i = 0; i < 1000; i++)
        {
            existingData.Add(new LargeScaleModel
            {
                TimestampCol = new DateTime(2024, 1, 1).AddHours(i),
                VarcharCol = $"Symbol_{i % 10}",
                BigintCol = i,
                Value = i * 1.5m
            });
        }
        db.InsertAll(existingData);

        // New batch of 70 records (internally unique, but some overlap with existing)
        var newBatch = new List<LargeScaleModel>();
        for (int i = 950; i < 1020; i++) // 50 duplicates, 20 new
        {
            newBatch.Add(new LargeScaleModel
            {
                TimestampCol = new DateTime(2024, 1, 1).AddHours(i),
                VarcharCol = $"Symbol_{i % 10}",
                BigintCol = i,
                Value = i * 2.0m // Different value (but should not be inserted if duplicate key)
            });
        }

        var sw = Stopwatch.StartNew();
        var insertedCount = db.BulkInsertWithDeduplication(
            newBatch,
            "TimestampCol", "VarcharCol", "BigintCol");
        sw.Stop();

        _output.WriteLine($"845M row scenario simulation: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Inserted {insertedCount} of {newBatch.Count} records (duplicates filtered: {newBatch.Count - insertedCount})");

        Assert.Equal(20, insertedCount); // Only 20 new records
        Assert.Equal(1020, db.Count<LargeScaleModel>()); // 1000 + 20 new

        // Verify main table was never corrupted (duplicates not overwritten)
        var record950 = db.Single<LargeScaleModel>(x =>
            x.TimestampCol == new DateTime(2024, 1, 1).AddHours(950) &&
            x.VarcharCol == "Symbol_0" &&
            x.BigintCol == 950);
        Assert.Equal(950 * 1.5m, record950.Value); // Original value preserved
    }
}

// Test Models

public class TimeSeriesData
{
    [AutoIncrement]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; }
    public decimal Value { get; set; }
}

public class UniqueAttributeModel
{
    [AutoIncrement]
    public int Id { get; set; }
    [Unique]
    public string Email { get; set; }
    public string Name { get; set; }
}

[CompositeIndex(nameof(Date), nameof(Symbol), nameof(Exchange), Unique = true)]
public class CompositeIndexModel
{
    [AutoIncrement]
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Symbol { get; set; }
    public string Exchange { get; set; }
    public decimal Price { get; set; }
}

public class TripleKeyModel
{
    [AutoIncrement]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string VarcharCol { get; set; }
    public long BigintCol { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// Model simulating the 845M row table scenario
/// </summary>
public class LargeScaleModel
{
    [AutoIncrement]
    public int Id { get; set; }
    public DateTime TimestampCol { get; set; }
    public string VarcharCol { get; set; }
    public long BigintCol { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// Model with multiple individual unique columns - should throw error on auto-detect
/// </summary>
public class MultipleUniqueModel
{
    [AutoIncrement]
    public int Id { get; set; }
    [Unique]
    public string Email { get; set; }
    [Unique]
    public string Username { get; set; }
    public string Name { get; set; }
}

/// <summary>
/// Model with multiple CompositeIndex(Unique=true) attributes - should throw error on auto-detect
/// </summary>
[CompositeIndex(nameof(Col1), nameof(Col2), Unique = true)]
[CompositeIndex(nameof(Col2), nameof(Col3), Unique = true)]
public class MultipleCompositeIndexModel
{
    [AutoIncrement]
    public int Id { get; set; }
    public string Col1 { get; set; }
    public string Col2 { get; set; }
    public string Col3 { get; set; }
}

/// <summary>
/// Model with CompositeKey AND individual Unique column - CompositeKey takes precedence
/// </summary>
[CompositeKey(nameof(Timestamp), nameof(Symbol))]
public class CompositeKeyWithUniqueModel
{
    [AutoIncrement]
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; }
    [Unique]
    public string TransactionId { get; set; }
    public decimal Value { get; set; }
}
