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
    public void BulkInsertWithDeduplication_LinqExpression_SingleColumn_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<UniqueAttributeModel>(overwrite: true);

        // Insert initial data
        db.InsertAll(new[]
        {
            new UniqueAttributeModel { Email = "user1@test.com", Name = "User 1" }
        });

        // Use LINQ expression for single column
        var newData = new[]
        {
            new UniqueAttributeModel { Email = "user1@test.com", Name = "Duplicate" }, // Duplicate
            new UniqueAttributeModel { Email = "user2@test.com", Name = "User 2" }     // New
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, x => x.Email);

        Assert.Equal(1, insertedCount);
        Assert.Equal(2, db.Count<UniqueAttributeModel>());
    }

    [Fact]
    public void BulkInsertWithDeduplication_LinqExpression_MultipleColumns_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        db.InsertAll(new[]
        {
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", Value = 100 }
        });

        // Use LINQ expression for multiple columns
        var newData = new[]
        {
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", Value = 999 }, // Duplicate
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 2), Symbol = "BTC", Value = 101 }  // New
        };

        var insertedCount = db.BulkInsertWithDeduplication(
            newData,
            x => new { x.Timestamp, x.Symbol }
        );

        Assert.Equal(1, insertedCount);
        Assert.Equal(2, db.Count<TimeSeriesData>());
    }

    [Fact]
    public async System.Threading.Tasks.Task BulkInsertWithDeduplication_LinqExpressionAsync_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        db.InsertAll(new[]
        {
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", Value = 100 }
        });

        var newData = new[]
        {
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 1), Symbol = "BTC", Value = 999 },
            new TimeSeriesData { Timestamp = new DateTime(2025, 1, 2), Symbol = "BTC", Value = 101 }
        };

        var insertedCount = await db.BulkInsertWithDeduplicationAsync(
            newData,
            x => new { x.Timestamp, x.Symbol }
        );

        Assert.Equal(1, insertedCount);
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
    public void BulkInsertWithDeduplication_InternalDuplicates_KeepsFirstOccurrenceOnly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Insert initial data
        var initialData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 }
        };
        db.InsertAll(initialData);

        // Try to insert data that has INTERNAL duplicates (duplicates within the incoming dataset)
        var newData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 999 }, // Duplicate with main table
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 201 }, // First occurrence (should be kept)
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 202 }, // Internal duplicate (should be ignored)
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Symbol = "BTC", Value = 203 }, // Internal duplicate (should be ignored)
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Symbol = "BTC", Value = 301 }, // First occurrence (should be kept)
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Symbol = "BTC", Value = 302 }, // Internal duplicate (should be ignored)
            new() { Timestamp = new DateTime(2025, 1, 1, 13, 0, 0), Symbol = "BTC", Value = 401 }  // Unique (should be kept)
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "Symbol");

        // Should insert 3 records:
        // - 11:00 BTC (first occurrence, value 201)
        // - 12:00 BTC (first occurrence, value 301)
        // - 13:00 BTC (unique, value 401)
        // Should NOT insert:
        // - 10:00 BTC (duplicate with main table)
        // - 11:00 BTC duplicates (values 202, 203)
        // - 12:00 BTC duplicate (value 302)
        Assert.Equal(3, insertedCount);

        var allRecords = db.Select<TimeSeriesData>().OrderBy(x => x.Timestamp).ToList();
        Assert.Equal(4, allRecords.Count); // 1 initial + 3 new

        // Verify only first occurrences were inserted
        var record11am = allRecords.First(r => r.Timestamp.Hour == 11);
        Assert.Equal(201, record11am.Value); // First occurrence (201), not 202 or 203

        var record12pm = allRecords.First(r => r.Timestamp.Hour == 12);
        Assert.Equal(301, record12pm.Value); // First occurrence (301), not 302

        var record1pm = allRecords.First(r => r.Timestamp.Hour == 13);
        Assert.Equal(401, record1pm.Value); // Unique value
    }

    [Fact]
    public void BulkInsertWithDeduplication_AllInternalDuplicates_InsertsOnlyFirst()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TimeSeriesData>(overwrite: true);

        // Try to insert data where ALL rows are internal duplicates
        var newData = new List<TimeSeriesData>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 100 },
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 200 },
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Symbol = "BTC", Value = 300 }
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "Symbol");

        Assert.Equal(1, insertedCount); // Only first occurrence inserted
        Assert.Equal(1, db.Count<TimeSeriesData>());

        // Verify first occurrence was kept
        var records = db.Select<TimeSeriesData>();
        Assert.Single(records);
        Assert.Equal(100, records.First().Value); // First value in the list
    }

    [Fact]
    public void BulkInsertWithDeduplication_InternalDuplicatesWithCompositeKey_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<TripleKeyModel>(overwrite: true);

        // Insert initial data
        var initialData = new List<TripleKeyModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 1, Value = 100 }
        };
        db.InsertAll(initialData);

        // Insert data with internal duplicates on the triple key
        var newData = new List<TripleKeyModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1), VarcharCol = "A", BigintCol = 1, Value = 999 }, // Duplicate with main
            new() { Timestamp = new DateTime(2025, 1, 2), VarcharCol = "B", BigintCol = 2, Value = 201 }, // First occurrence
            new() { Timestamp = new DateTime(2025, 1, 2), VarcharCol = "B", BigintCol = 2, Value = 202 }, // Internal duplicate
            new() { Timestamp = new DateTime(2025, 1, 2), VarcharCol = "B", BigintCol = 2, Value = 203 }, // Internal duplicate
            new() { Timestamp = new DateTime(2025, 1, 3), VarcharCol = "C", BigintCol = 3, Value = 301 }  // Unique
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp", "VarcharCol", "BigintCol");

        Assert.Equal(2, insertedCount); // Only 2 new unique records
        Assert.Equal(3, db.Count<TripleKeyModel>());

        // Verify first occurrences were kept
        var results = db.Select<TripleKeyModel>().OrderBy(x => x.Timestamp).ToList();
        var record2 = results.First(r => r.Timestamp == new DateTime(2025, 1, 2));
        Assert.Equal(201, record2.Value); // First occurrence, not 202 or 203
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
    public void BulkInsertWithDeduplication_WithPrimaryKeyConstraint_AllowsDuplicatesInStaging()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<PrimaryKeyModel>(overwrite: true);

        // Insert initial data with PRIMARY KEY
        db.Insert(new PrimaryKeyModel { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Value = 100 });

        // Try to insert data with duplicates on the PRIMARY KEY column
        // This should NOT fail even though Timestamp is PRIMARY KEY
        // because staging table should not have constraints
        var newData = new List<PrimaryKeyModel>
        {
            new() { Timestamp = new DateTime(2025, 1, 1, 10, 0, 0), Value = 999 }, // Duplicate PK (should be filtered)
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Value = 201 }, // First occurrence
            new() { Timestamp = new DateTime(2025, 1, 1, 11, 0, 0), Value = 202 }, // Internal duplicate on PK
            new() { Timestamp = new DateTime(2025, 1, 1, 12, 0, 0), Value = 301 }  // Unique
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Timestamp");

        Assert.Equal(2, insertedCount); // Only 11:00 and 12:00 inserted
        Assert.Equal(3, db.Count<PrimaryKeyModel>());

        var records = db.Select<PrimaryKeyModel>().OrderBy(x => x.Timestamp).ToList();
        Assert.Equal(100, records.First(r => r.Timestamp.Hour == 10).Value); // Original preserved
        Assert.Equal(201, records.First(r => r.Timestamp.Hour == 11).Value); // First occurrence kept
    }

    [Fact]
    public void BulkInsertWithDeduplication_WithUniqueConstraint_AllowsDuplicatesInStaging()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<UniqueConstraintModel>(overwrite: true);

        // Insert initial data
        db.Insert(new UniqueConstraintModel { Code = "ABC", Name = "Original" });

        // Try to insert data with duplicates on UNIQUE column
        var newData = new List<UniqueConstraintModel>
        {
            new() { Code = "ABC", Name = "Duplicate" },  // Duplicate on UNIQUE (filtered)
            new() { Code = "DEF", Name = "First" },      // First occurrence
            new() { Code = "DEF", Name = "Second" },     // Internal duplicate on UNIQUE
            new() { Code = "GHI", Name = "Unique" }      // Unique
        };

        var insertedCount = db.BulkInsertWithDeduplication(newData, "Code");

        Assert.Equal(2, insertedCount); // Only DEF and GHI inserted
        Assert.Equal(3, db.Count<UniqueConstraintModel>());

        var abc = db.Single<UniqueConstraintModel>(x => x.Code == "ABC");
        Assert.Equal("Original", abc.Name); // Original preserved, not overwritten
    }

    [Fact]
    public async System.Threading.Tasks.Task BulkInsertWithDeduplication_DescribeDoesNotBlock_DuringInsert()
    {
        // Use file-based DB so both connections see same database
        var dbPath = $"/tmp/health_check_test_{Guid.NewGuid():N}.db";
        var testFactory = new DuckDbOrmLiteConnectionFactory($"Data Source={dbPath}");

        try
        {
            using var db = testFactory.Open();
            db.CreateTable<TimeSeriesData>(overwrite: true);

            // Insert some initial data
            var initialData = Enumerable.Range(1, 100).Select(i => new TimeSeriesData
            {
                Timestamp = new DateTime(2025, 1, 1).AddMinutes(i),
                Symbol = "BTC",
                Value = i
            }).ToList();
            db.InsertAll(initialData);

            // Prepare larger bulk insert data to ensure it takes measurable time
            var bulkData = Enumerable.Range(1000, 50000).Select(i => new TimeSeriesData
            {
                Timestamp = new DateTime(2025, 1, 1).AddMinutes(i),
                Symbol = "BTC",
                Value = i
            }).ToList();

            // Track if DESCRIBE succeeded
            var describeSucceeded = false;
            var insertWasRunning = false;
            Exception describeException = null;

            // Start bulk insert in background task (keeps connection 1 busy)
            var insertStarted = new System.Threading.ManualResetEventSlim(false);
            var bulkInsertTask = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    insertStarted.Set(); // Signal that we're about to start
                    db.BulkInsertWithDeduplication(bulkData, "Timestamp", "Symbol");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Bulk insert error: {ex.Message}");
                }
            });

            // Wait for insert to definitely start
            insertStarted.Wait();
            await System.Threading.Tasks.Task.Delay(50); // Give it time to get into INSERT SELECT

            // While bulk insert is running, try DESCRIBE on separate connection (health check simulation)
            using var healthCheckDb = testFactory.Open();
            try
            {
                insertWasRunning = !bulkInsertTask.IsCompleted; // Check BEFORE DESCRIBE

                var schema = healthCheckDb.SqlList<Dictionary<string, object>>("DESCRIBE TimeSeriesData");

                describeSucceeded = schema.Count > 0;

                _output.WriteLine($"DESCRIBE returned {schema.Count} columns. Insert was running: {insertWasRunning}, Insert still running: {!bulkInsertTask.IsCompleted}");
            }
            catch (Exception ex)
            {
                describeException = ex;
                _output.WriteLine($"DESCRIBE failed: {ex.Message}");
            }

            // Wait for bulk insert to complete
            await bulkInsertTask;

            // Assertions - DESCRIBE should succeed regardless of timing
            Assert.True(describeSucceeded, "DESCRIBE should succeed (even if insert finished quickly)");
            Assert.Null(describeException);

            // Note: We don't assert insertWasRunning because the insert might be very fast
            // The key assertion is that DESCRIBE succeeded without blocking/erroring
        }
        finally
        {
            // Cleanup
            if (System.IO.File.Exists(dbPath))
                System.IO.File.Delete(dbPath);
        }
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

/// <summary>
/// Model with PRIMARY KEY on Timestamp column
/// </summary>
public class PrimaryKeyModel
{
    [PrimaryKey]
    public DateTime Timestamp { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// Model with UNIQUE constraint on Code column
/// </summary>
public class UniqueConstraintModel
{
    [AutoIncrement]
    public int Id { get; set; }
    [Unique]
    public string Code { get; set; }
    public string Name { get; set; }
}
