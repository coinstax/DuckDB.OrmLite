using System;
using System.IO;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;
using Xunit.Abstractions;

namespace DuckDB.OrmLite.Tests;

public class GenericFactoryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _mainDb;
    private readonly string _archive2024;
    private readonly string _archive2023;

    public class CryptoPrice
    {
        [AutoIncrement]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public GenericFactoryTests(ITestOutputHelper output)
    {
        _output = output;
        var tempDir = Path.Combine(Path.GetTempPath(), $"generic_factory_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        _mainDb = Path.Combine(tempDir, "main.db");
        _archive2024 = Path.Combine(tempDir, "archive_2024.db");
        _archive2023 = Path.Combine(tempDir, "archive_2023.db");

        _output.WriteLine($"Test databases:");
        _output.WriteLine($"  Main: {_mainDb}");
        _output.WriteLine($"  Archive 2024: {_archive2024}");
        _output.WriteLine($"  Archive 2023: {_archive2023}");
    }

    public void Dispose()
    {
        try
        {
            foreach (var dbPath in new[] { _mainDb, _archive2024, _archive2023 })
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }

            var dir = Path.GetDirectoryName(_mainDb);
            if (dir != null && Directory.Exists(dir))
            {
                Directory.Delete(dir);
            }

            _output.WriteLine("Cleaned up test databases");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up: {ex.Message}");
        }
    }

    [Fact]
    public void GenericFactory_AutomaticallyConfiguresMultiDatabaseTable()
    {
        // Setup: Create test databases with data
        SetupTestData();

        // Test: Create generic factory - should automatically configure CryptoPrice as multi-db table
        var factory = new DuckDbOrmLiteConnectionFactory<CryptoPrice>($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archive2024, _archive2023);

        using var db = factory.Open();

        // Should query across all databases automatically
        var allPrices = db.Select<CryptoPrice>();
        _output.WriteLine($"Total records: {allPrices.Count}");

        Assert.Equal(9, allPrices.Count); // 3 from main + 3 from 2024 + 3 from 2023

        var btcPrices = db.Select<CryptoPrice>(x => x.Symbol == "BTC");
        _output.WriteLine($"BTC records: {btcPrices.Count}");
        Assert.Equal(3, btcPrices.Count);
    }

    [Fact]
    public void GenericFactory_ComparedToNonGeneric_SameBehavior()
    {
        SetupTestData();

        // Generic factory
        var genericFactory = new DuckDbOrmLiteConnectionFactory<CryptoPrice>($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archive2024, _archive2023);

        // Non-generic factory with explicit configuration
        var nonGenericFactory = new DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archive2024, _archive2023)
            .WithMultiDatabaseTable<CryptoPrice>();

        // Both should return same results
        using (var db1 = genericFactory.Open())
        using (var db2 = nonGenericFactory.Open())
        {
            var results1 = db1.Select<CryptoPrice>();
            var results2 = db2.Select<CryptoPrice>();

            _output.WriteLine($"Generic factory results: {results1.Count}");
            _output.WriteLine($"Non-generic factory results: {results2.Count}");

            Assert.Equal(results2.Count, results1.Count);
            Assert.Equal(9, results1.Count);
        }
    }

    [Fact]
    public void GenericFactory_OpenForWrite_WritesToMainDatabase()
    {
        SetupTestData();

        var factory = new DuckDbOrmLiteConnectionFactory<CryptoPrice>($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archive2024, _archive2023);

        // Write new record
        using (var db = factory.OpenForWrite())
        {
            db.Insert(new CryptoPrice
            {
                Date = DateTime.Today,
                Symbol = "DOGE",
                Price = 0.42m
            });
        }

        // Verify it went to main database only
        var mainFactory = new DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var mainDb = mainFactory.Open())
        {
            var dogeCount = mainDb.Count<CryptoPrice>(x => x.Symbol == "DOGE");
            _output.WriteLine($"DOGE records in main.db: {dogeCount}");
            Assert.Equal(1, dogeCount);
        }

        // Verify archives unchanged
        var archive2024Factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_archive2024}");
        using (var archiveDb = archive2024Factory.Open())
        {
            var dogeCount = archiveDb.Count<CryptoPrice>(x => x.Symbol == "DOGE");
            _output.WriteLine($"DOGE records in archive_2024.db: {dogeCount}");
            Assert.Equal(0, dogeCount);
        }
    }

    [Fact]
    public void GenericFactory_SimplifiedSyntax()
    {
        SetupTestData();

        // Very clean, concise syntax
        var factory = new DuckDbOrmLiteConnectionFactory<CryptoPrice>($"Data Source={_mainDb}")
            .WithAdditionalDatabases(_archive2024, _archive2023);

        using var db = factory.Open();
        var prices = db.Select<CryptoPrice>(x => x.Price > 30000);

        _output.WriteLine($"Found {prices.Count} prices > $30,000");
        Assert.True(prices.Count > 0);
    }

    private void SetupTestData()
    {
        // Create main database
        var mainFactory = new DuckDbOrmLiteConnectionFactory($"Data Source={_mainDb}");
        using (var db = mainFactory.Open())
        {
            db.CreateTable<CryptoPrice>(overwrite: true);
            db.Insert(new CryptoPrice { Date = new DateTime(2025, 1, 1), Symbol = "BTC", Price = 95000m });
            db.Insert(new CryptoPrice { Date = new DateTime(2025, 1, 2), Symbol = "ETH", Price = 3500m });
            db.Insert(new CryptoPrice { Date = new DateTime(2025, 1, 3), Symbol = "BNB", Price = 600m });
        }

        // Create 2024 archive
        var archive2024Factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_archive2024}");
        using (var db = archive2024Factory.Open())
        {
            db.CreateTable<CryptoPrice>(overwrite: true);
            db.Insert(new CryptoPrice { Date = new DateTime(2024, 12, 1), Symbol = "BTC", Price = 92000m });
            db.Insert(new CryptoPrice { Date = new DateTime(2024, 12, 2), Symbol = "ETH", Price = 3400m });
            db.Insert(new CryptoPrice { Date = new DateTime(2024, 12, 3), Symbol = "BNB", Price = 580m });
        }

        // Create 2023 archive
        var archive2023Factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_archive2023}");
        using (var db = archive2023Factory.Open())
        {
            db.CreateTable<CryptoPrice>(overwrite: true);
            db.Insert(new CryptoPrice { Date = new DateTime(2023, 12, 1), Symbol = "BTC", Price = 42000m });
            db.Insert(new CryptoPrice { Date = new DateTime(2023, 12, 2), Symbol = "ETH", Price = 2200m });
            db.Insert(new CryptoPrice { Date = new DateTime(2023, 12, 3), Symbol = "BNB", Price = 320m });
        }

        _output.WriteLine("Test data setup complete");
    }
}
