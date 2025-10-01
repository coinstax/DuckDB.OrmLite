using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;

namespace DuckDbOrmLite.Tests;

[Collection("DuckDB Tests")]
public class MultiDatabaseTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _mainDb;
    private readonly string _archiveDb2024;
    private readonly string _archiveDb2023;

    public MultiDatabaseTests()
    {
        // Create temporary directory for test databases
        _testDir = Path.Combine(Path.GetTempPath(), "duckdb_multidb_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);

        _mainDb = Path.Combine(_testDir, "main.db");
        _archiveDb2024 = Path.Combine(_testDir, "archive_2024.db");
        _archiveDb2023 = Path.Combine(_testDir, "archive_2023.db");

        // Create and populate test databases
        SetupTestDatabases();
    }

    private void SetupTestDatabases()
    {
        // Main database (2025 data)
        var mainFactory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var db = mainFactory.OpenDbConnection())
        {
            db.CreateTable<PriceData>();
            db.InsertAll(new[]
            {
                new PriceData { Date = new DateTime(2025, 1, 1), Symbol = "BTC", Price = 100000 },
                new PriceData { Date = new DateTime(2025, 1, 2), Symbol = "BTC", Price = 101000 },
                new PriceData { Date = new DateTime(2025, 1, 1), Symbol = "ETH", Price = 5000 },
            });
        }

        // Archive 2024
        var archive2024Factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_archiveDb2024}");
        using (var db = archive2024Factory.OpenDbConnection())
        {
            db.CreateTable<PriceData>();
            db.InsertAll(new[]
            {
                new PriceData { Date = new DateTime(2024, 12, 31), Symbol = "BTC", Price = 95000 },
                new PriceData { Date = new DateTime(2024, 12, 30), Symbol = "BTC", Price = 94000 },
                new PriceData { Date = new DateTime(2024, 12, 31), Symbol = "ETH", Price = 4800 },
            });
        }

        // Archive 2023
        var archive2023Factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_archiveDb2023}");
        using (var db = archive2023Factory.OpenDbConnection())
        {
            db.CreateTable<PriceData>();
            db.InsertAll(new[]
            {
                new PriceData { Date = new DateTime(2023, 12, 31), Symbol = "BTC", Price = 42000 },
                new PriceData { Date = new DateTime(2023, 12, 30), Symbol = "BTC", Price = 41000 },
                new PriceData { Date = new DateTime(2023, 12, 31), Symbol = "ETH", Price = 2200 },
            });
        }
    }

    public void Dispose()
    {
        // Clean up test databases
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void SingleDatabase_BackwardCompatibility()
    {
        // Test that single-database mode still works (no multi-db configuration)
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");

        using var db = factory.Open();
        var results = db.Select<PriceData>();

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(2025, r.Date.Year));
    }

    [Fact]
    public void MultiDatabase_SelectAcrossAllDatabases()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var results = db.Select<PriceData>();

        // Should have data from all three databases
        Assert.Equal(9, results.Count); // 3 from main + 3 from 2024 + 3 from 2023

        // Verify data from each year
        Assert.Contains(results, r => r.Date.Year == 2025);
        Assert.Contains(results, r => r.Date.Year == 2024);
        Assert.Contains(results, r => r.Date.Year == 2023);
    }

    [Fact]
    public void MultiDatabase_SelectWithWhere()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var results = db.Select<PriceData>(x => x.Symbol == "BTC");

        // Should have BTC data from all three databases
        Assert.Equal(6, results.Count); // 2 from main + 2 from 2024 + 2 from 2023
        Assert.All(results, r => Assert.Equal("BTC", r.Symbol));
    }

    [Fact]
    public void MultiDatabase_SelectWithComplexPredicate()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var results = db.Select<PriceData>(x => x.Symbol == "ETH" && x.Price > 3000);

        // Should have ETH data from 2025 and 2024 (both > 3000)
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("ETH", r.Symbol));
        Assert.All(results, r => Assert.True(r.Price > 3000));
    }

    [Fact]
    public void MultiDatabase_Count()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var count = db.Count<PriceData>();

        Assert.Equal(9, count);
    }

    [Fact]
    public void MultiDatabase_CountWithWhere()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var count = db.Count<PriceData>(x => x.Symbol == "BTC");

        Assert.Equal(6, count);
    }

    [Fact]
    public void MultiDatabase_Aggregations()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();

        // Test SUM
        var totalPrice = db.Scalar<decimal>(
            db.From<PriceData>().Select(x => Sql.Sum(x.Price))
        );
        Assert.True(totalPrice > 0);

        // Test AVG
        var avgPrice = db.Scalar<decimal>(
            db.From<PriceData>()
                .Where(x => x.Symbol == "BTC")
                .Select(x => Sql.Avg(x.Price))
        );
        Assert.True(avgPrice > 0);

        // Test MAX
        var maxPrice = db.Scalar<decimal>(
            db.From<PriceData>().Select(x => Sql.Max(x.Price))
        );
        Assert.Equal(101000, maxPrice); // Highest price from 2025
    }

    [Fact]
    public void MultiDatabase_OrderBy()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var results = db.Select(db.From<PriceData>()
            .Where(x => x.Symbol == "BTC")
            .OrderBy(x => x.Date));

        Assert.Equal(6, results.Count);
        // Verify chronological order
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i].Date >= results[i - 1].Date);
        }
    }

    [Fact]
    public void MultiDatabase_WithMultiDatabaseTableGeneric()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTable<PriceData>();

        using var db = factory.Open();
        var results = db.Select<PriceData>();

        Assert.Equal(9, results.Count);
    }

    [Fact]
    public void OpenForWrite_WritesToMainDatabaseOnly()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using (var db = factory.OpenForWrite())
        {
            db.Insert(new PriceData
            {
                Date = new DateTime(2025, 1, 10),
                Symbol = "DOGE",
                Price = 0.42m
            });
        }

        // Verify data is only in main database
        var mainFactory2 = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var mainConn = mainFactory2.OpenDbConnection())
        {
            var count = mainConn.SqlScalar<long>("SELECT COUNT(*) FROM PriceData WHERE Symbol = 'DOGE'");
            Assert.Equal(1, count);
        }

        // Verify data is NOT in archive databases
        var archiveFactory2 = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_archiveDb2024}");
        using (var archiveConn = archiveFactory2.OpenDbConnection())
        {
            var count = archiveConn.SqlScalar<long>("SELECT COUNT(*) FROM PriceData WHERE Symbol = 'DOGE'");
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public void OpenForWrite_UpdatesMainDatabaseOnly()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using (var db = factory.OpenForWrite())
        {
            var record = db.Single<PriceData>(x => x.Date == new DateTime(2025, 1, 1) && x.Symbol == "BTC");
            record.Price = 999999;
            db.Update(record);
        }

        // Verify update in main database
        var mainFactory3 = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var mainConn = mainFactory3.OpenDbConnection())
        {
            var price = mainConn.SqlScalar<decimal>(
                "SELECT Price FROM PriceData WHERE Date = '2025-01-01' AND Symbol = 'BTC'"
            );
            Assert.Equal(999999, price);
        }
    }

    [Fact]
    public void MultiDatabase_MissingDatabaseFile_ThrowsException()
    {
        var nonExistentDb = Path.Combine(_testDir, "nonexistent.db");

        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(nonExistentDb)
            .WithMultiDatabaseTables("PriceData");

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var db = factory.Open();
        });
    }

    [Fact]
    public void MultiDatabase_TableExistsOnlyInSomeDatabases()
    {
        // Create a database without the PriceData table
        var emptyDb = Path.Combine(_testDir, "empty.db");
        var emptyFactory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={emptyDb}");
        using (var conn = emptyFactory.OpenDbConnection())
        {
            // Don't create the table - just open and close to create the file
        }

        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(emptyDb, _archiveDb2024)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();
        var results = db.Select<PriceData>();

        // Should only get data from main and archive_2024 (not empty)
        Assert.Equal(6, results.Count); // 3 from main + 3 from 2024
    }

    [Fact]
    public void MultiDatabase_DatabaseAliasSanitization()
    {
        // Test database names with special characters
        var specialDb = Path.Combine(_testDir, "archive-2022.backup.db");

        var specialFactory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={specialDb}");
        using (var db = specialFactory.OpenDbConnection())
        {
            db.CreateTable<PriceData>();
            db.Insert(new PriceData { Date = new DateTime(2022, 1, 1), Symbol = "BTC", Price = 30000 });
        }

        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(specialDb)
            .WithMultiDatabaseTables("PriceData");

        using var db2 = factory.Open();
        var results = db2.Select<PriceData>();

        // Should include data from all databases
        Assert.Contains(results, r => r.Price == 30000);
    }

    [Fact]
    public void MultiDatabase_MultipleConnections()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        // Open multiple connections sequentially
        using (var db1 = factory.Open())
        {
            var count1 = db1.Count<PriceData>();
            Assert.Equal(9, count1);
        }

        using (var db2 = factory.Open())
        {
            var count2 = db2.Count<PriceData>();
            Assert.Equal(9, count2);
        }
    }

    [Fact]
    public void MultiDatabase_NonMultiDbTable_UsesDirectTable()
    {
        // Create a table that's not configured for multi-db
        var tempFactory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var db = tempFactory.OpenDbConnection())
        {
            db.CreateTable<NonMultiDbTable>();
            db.Insert(new NonMultiDbTable { Name = "Test", Value = 123 });
        }

        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData"); // Only PriceData is multi-db

        using var db2 = factory.Open();

        // PriceData should use unified view (9 records across all DBs)
        var priceCount = db2.Count<PriceData>();
        Assert.Equal(9, priceCount);

        // NonMultiDbTable should use direct table (only main DB)
        var nonMultiCount = db2.Count<NonMultiDbTable>();
        Assert.Equal(1, nonMultiCount);
    }

    [Fact]
    public async Task MultiDatabase_AsyncQueries()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData");

        using var db = factory.Open();

        // Test async SELECT
        var results = await db.SelectAsync<PriceData>(x => x.Symbol == "BTC");
        Assert.Equal(6, results.Count);

        // Test async COUNT
        var count = await db.CountAsync<PriceData>();
        Assert.Equal(9, count);
    }

    [Fact]
    public void MultiDatabase_WithAutoConfigureViewsDisabled()
    {
        var factory = new DuckDB.OrmLite.DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archiveDb2024, _archiveDb2023)
            .WithMultiDatabaseTables("PriceData")
            .WithAutoConfigureViews(false);

        using var db = factory.Open();

        // Should only see data from main database (views not created)
        var count = db.Count<PriceData>();
        Assert.Equal(3, count); // Only main database
    }
}

// Test models
public class PriceData
{
    [AutoIncrement]
    public int Id { get; set; }

    public DateTime Date { get; set; }

    public string Symbol { get; set; } = "";

    public decimal Price { get; set; }
}

public class NonMultiDbTable
{
    [AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int Value { get; set; }
}
