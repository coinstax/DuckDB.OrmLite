using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;
using Xunit.Abstractions;

namespace DuckDB.OrmLite.Tests;

public class ConcurrencyConflictTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public class PriceRecord
    {
        [AutoIncrement]
        public int Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime LastUpdated { get; set; }
        public int UpdateCount { get; set; }
    }

    public ConcurrencyConflictTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"concurrency_conflict_test_{Guid.NewGuid()}.db");
        _output.WriteLine($"Test database: {_testDbPath}");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
                _output.WriteLine($"Cleaned up test database: {_testDbPath}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error cleaning up: {ex.Message}");
        }
    }

    [Fact]
    public void CreateLargeDatabase_WithManyRecords()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceRecord>(overwrite: true);

            _output.WriteLine("Inserting 10,000 records...");

            var records = new List<PriceRecord>();
            var symbols = new[] { "BTC", "ETH", "USDT", "BNB", "XRP", "ADA", "DOGE", "SOL", "MATIC", "DOT" };

            for (int i = 0; i < 10000; i++)
            {
                records.Add(new PriceRecord
                {
                    Symbol = symbols[i % symbols.Length],
                    Price = 1000 + (i * 0.5m),
                    LastUpdated = DateTime.UtcNow.AddHours(-i),
                    UpdateCount = 0
                });

                if (records.Count == 1000)
                {
                    db.InsertAll(records);
                    records.Clear();
                    _output.WriteLine($"  Inserted {(i + 1)} records...");
                }
            }

            if (records.Any())
            {
                db.InsertAll(records);
            }

            var count = db.Count<PriceRecord>();
            _output.WriteLine($"✅ Created database with {count} records");

            Assert.Equal(10000, count);
        }
    }

    [Fact]
    public async Task MultipleThreads_UpdateSameRecord_ShouldConflict()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create database with records
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceRecord>(overwrite: true);

            // Insert 1000 records
            var records = new List<PriceRecord>();
            for (int i = 1; i <= 1000; i++)
            {
                records.Add(new PriceRecord
                {
                    Symbol = $"SYM{i}",
                    Price = 100m,
                    LastUpdated = DateTime.UtcNow,
                    UpdateCount = 0
                });
            }
            db.InsertAll(records);

            _output.WriteLine($"Created database with {db.Count<PriceRecord>()} records");
        }

        var errors = new List<Exception>();
        var successCount = 0;
        var threads = 10;
        var targetRecordId = 500; // All threads will update the SAME record
        var openTimes = new List<(int threadNum, long openTimeMs)>();

        _output.WriteLine($"\nStarting {threads} threads, ALL updating record ID {targetRecordId}...");

        var tasks = Enumerable.Range(0, threads).Select(threadNum => Task.Run(() =>
        {
            try
            {
                var openStart = DateTime.UtcNow;
                using (var db = factory.OpenForWrite())
                {
                    var openEnd = DateTime.UtcNow;
                    var openTimeMs = (long)(openEnd - openStart).TotalMilliseconds;

                    lock (openTimes)
                    {
                        openTimes.Add((threadNum, openTimeMs));
                    }

                    _output.WriteLine($"Thread {threadNum}: OpenForWrite() took {openTimeMs}ms");

                    // Each thread tries to update the same record 10 times
                    for (int i = 0; i < 10; i++)
                    {
                        var record = db.SingleById<PriceRecord>(targetRecordId);
                        record.Price += 1m;
                        record.UpdateCount++;
                        record.LastUpdated = DateTime.UtcNow;

                        db.Update(record);

                        // Small delay
                        Thread.Sleep(10);
                    }

                    Interlocked.Increment(ref successCount);
                    _output.WriteLine($"Thread {threadNum}: Completed 10 updates");
                }
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
                _output.WriteLine($"Thread {threadNum}: ERROR - {ex.GetType().Name}: {ex.Message}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        _output.WriteLine($"\n=== OpenForWrite() Timing ===");
        foreach (var (threadNum, openTimeMs) in openTimes.OrderBy(x => x.threadNum))
        {
            _output.WriteLine($"Thread {threadNum}: {openTimeMs}ms");
        }

        var maxOpenTime = openTimes.Max(x => x.openTimeMs);
        if (maxOpenTime > 100)
        {
            _output.WriteLine($"\n⚠️  Some OpenForWrite() calls took >{maxOpenTime}ms - likely blocked");
        }
        else
        {
            _output.WriteLine($"\n✅ All OpenForWrite() calls completed quickly (<{maxOpenTime}ms) - no blocking detected");
        }

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Successful threads: {successCount}/{threads}");
        _output.WriteLine($"Errors: {errors.Count}");

        foreach (var error in errors)
        {
            _output.WriteLine($"  - {error.GetType().Name}: {error.Message}");
        }

        // Check final state of the record
        using (var db = factory.OpenForWrite())
        {
            var finalRecord = db.SingleById<PriceRecord>(targetRecordId);
            _output.WriteLine($"\nFinal record state:");
            _output.WriteLine($"  Price: {finalRecord.Price} (expected: 200 if all updates succeeded)");
            _output.WriteLine($"  UpdateCount: {finalRecord.UpdateCount} (expected: 100 if all updates succeeded)");

            if (errors.Any())
            {
                _output.WriteLine("\n⚠️  CONCURRENT UPDATES TO SAME RECORD FAILED - Write conflicts detected");
            }
            else if (finalRecord.UpdateCount < 100)
            {
                _output.WriteLine($"\n⚠️  LOST UPDATES - Only {finalRecord.UpdateCount}/100 updates persisted (race condition)");
            }
            else
            {
                _output.WriteLine("\n✅ ALL CONCURRENT UPDATES SUCCEEDED - No conflicts");
            }
        }
    }

    [Fact]
    public async Task MultipleThreads_UpdateDifferentRecords_ShouldNotConflict()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create database with records
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceRecord>(overwrite: true);

            var records = new List<PriceRecord>();
            for (int i = 1; i <= 1000; i++)
            {
                records.Add(new PriceRecord
                {
                    Symbol = $"SYM{i}",
                    Price = 100m,
                    LastUpdated = DateTime.UtcNow,
                    UpdateCount = 0
                });
            }
            db.InsertAll(records);

            _output.WriteLine($"Created database with {db.Count<PriceRecord>()} records");
        }

        var errors = new List<Exception>();
        var successCount = 0;
        var threads = 10;

        _output.WriteLine($"\nStarting {threads} threads, each updating DIFFERENT records...");

        var tasks = Enumerable.Range(0, threads).Select(threadNum => Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    // Each thread updates its own set of records (no overlap)
                    var startId = (threadNum * 100) + 1;
                    var endId = startId + 100;

                    _output.WriteLine($"Thread {threadNum}: Updating records {startId}-{endId}");

                    for (int id = startId; id < endId; id++)
                    {
                        var record = db.SingleById<PriceRecord>(id);
                        record.Price += 10m;
                        record.UpdateCount++;
                        record.LastUpdated = DateTime.UtcNow;

                        db.Update(record);
                    }

                    Interlocked.Increment(ref successCount);
                    _output.WriteLine($"Thread {threadNum}: Completed 100 updates");
                }
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
                _output.WriteLine($"Thread {threadNum}: ERROR - {ex.GetType().Name}: {ex.Message}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Successful threads: {successCount}/{threads}");
        _output.WriteLine($"Errors: {errors.Count}");

        foreach (var error in errors)
        {
            _output.WriteLine($"  - {error.GetType().Name}: {error.Message}");
        }

        // Verify all records were updated correctly
        using (var db = factory.OpenForWrite())
        {
            var updatedRecords = db.Select<PriceRecord>(x => x.UpdateCount == 1);
            _output.WriteLine($"\nRecords with UpdateCount=1: {updatedRecords.Count}/1000");

            if (errors.Any())
            {
                _output.WriteLine("\n⚠️  CONCURRENT UPDATES TO DIFFERENT RECORDS FAILED");
            }
            else if (updatedRecords.Count < 1000)
            {
                _output.WriteLine($"\n⚠️  SOME UPDATES LOST - Only {updatedRecords.Count}/1000 updated");
            }
            else
            {
                _output.WriteLine("\n✅ ALL CONCURRENT UPDATES TO DIFFERENT RECORDS SUCCEEDED");
            }
        }
    }

    [Fact]
    public async Task MultipleThreads_UpdateSameRecordWithTransaction_CheckConflicts()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create database with records
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceRecord>(overwrite: true);

            var records = new List<PriceRecord>();
            for (int i = 1; i <= 100; i++)
            {
                records.Add(new PriceRecord
                {
                    Symbol = $"SYM{i}",
                    Price = 100m,
                    LastUpdated = DateTime.UtcNow,
                    UpdateCount = 0
                });
            }
            db.InsertAll(records);

            _output.WriteLine($"Created database with {db.Count<PriceRecord>()} records");
        }

        var errors = new List<Exception>();
        var successCount = 0;
        var threads = 5;
        var targetRecordId = 50;

        _output.WriteLine($"\nStarting {threads} threads with TRANSACTIONS, all updating record ID {targetRecordId}...");

        var tasks = Enumerable.Range(0, threads).Select(threadNum => Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    _output.WriteLine($"Thread {threadNum}: Starting transaction");

                    using (var trans = db.OpenTransaction())
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            var record = db.SingleById<PriceRecord>(targetRecordId);
                            record.Price += 1m;
                            record.UpdateCount++;
                            record.LastUpdated = DateTime.UtcNow;

                            db.Update(record);

                            // Longer delay to increase chance of conflict
                            Thread.Sleep(50);
                        }

                        trans.Commit();
                    }

                    Interlocked.Increment(ref successCount);
                    _output.WriteLine($"Thread {threadNum}: Transaction committed successfully");
                }
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
                _output.WriteLine($"Thread {threadNum}: ERROR - {ex.GetType().Name}: {ex.Message}");
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Successful threads: {successCount}/{threads}");
        _output.WriteLine($"Errors: {errors.Count}");

        foreach (var error in errors)
        {
            _output.WriteLine($"  - {error.GetType().Name}: {error.Message}");
        }

        // Check final state
        using (var db = factory.OpenForWrite())
        {
            var finalRecord = db.SingleById<PriceRecord>(targetRecordId);
            _output.WriteLine($"\nFinal record state:");
            _output.WriteLine($"  Price: {finalRecord.Price}");
            _output.WriteLine($"  UpdateCount: {finalRecord.UpdateCount}");
            _output.WriteLine($"  Expected if all succeeded: Price=125, UpdateCount=25");

            if (errors.Any())
            {
                _output.WriteLine("\n⚠️  TRANSACTION CONFLICTS DETECTED - Some transactions failed");
            }
            else if (finalRecord.UpdateCount < 25)
            {
                _output.WriteLine($"\n⚠️  LOST UPDATES IN TRANSACTIONS - Only {finalRecord.UpdateCount}/25 updates persisted");
            }
            else
            {
                _output.WriteLine("\n✅ ALL TRANSACTIONAL UPDATES SUCCEEDED");
            }
        }
    }
}
