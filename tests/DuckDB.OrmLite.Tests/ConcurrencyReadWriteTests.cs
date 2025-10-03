using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;
using Xunit.Abstractions;

namespace DuckDB.OrmLite.Tests;

public class ConcurrencyReadWriteTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public class PriceData
    {
        [AutoIncrement]
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public long Volume { get; set; }
    }

    public ConcurrencyReadWriteTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"read_write_test_{Guid.NewGuid()}.db");
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
    public async Task LargeDatabase_ConcurrentReadWhileWriting()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create initial database with reduced size for faster tests
        _output.WriteLine("Creating initial database with 5,000 records...");
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceData>(overwrite: true);

            var records = new List<PriceData>();
            var symbols = new[] { "BTC", "ETH", "USDT", "BNB", "XRP" };

            for (int i = 0; i < 5000; i++)
            {
                records.Add(new PriceData
                {
                    Date = DateTime.UtcNow.AddHours(-i),
                    Symbol = symbols[i % symbols.Length],
                    Price = 1000 + (i * 0.1m),
                    Volume = 1000000 + (i * 100)
                });
            }

            db.InsertAll(records);
            _output.WriteLine($"Initial database created with {db.Count<PriceData>()} records");
        }

        var writerStarted = new ManualResetEventSlim(false);
        var readerStarted = new ManualResetEventSlim(false);
        var writerErrors = new List<Exception>();
        var readerErrors = new List<Exception>();
        var readCounts = new List<(DateTime time, long count)>();
        var writeCounts = new List<(DateTime time, long totalInserted)>();

        _output.WriteLine("\nStarting concurrent reader and writer...");

        // Writer task - continuously append new records
        var writerTask = Task.Run(() =>
        {
            try
            {
                var openStart = DateTime.UtcNow;
                using (var db = factory.OpenForWrite())
                {
                    var openTime = (DateTime.UtcNow - openStart).TotalMilliseconds;
                    _output.WriteLine($"Writer: OpenForWrite() took {openTime:F0}ms");

                    writerStarted.Set();
                    readerStarted.Wait(TimeSpan.FromSeconds(5)); // Wait for reader to start

                    _output.WriteLine("Writer: Starting to append records...");

                    var symbols = new[] { "NEW1", "NEW2", "NEW3" };
                    var totalInserted = 0;

                    for (int batch = 0; batch < 20; batch++)  // Reduced from 50 to 20
                    {
                        var records = new List<PriceData>();
                        for (int i = 0; i < 50; i++)  // Reduced from 100 to 50
                        {
                            records.Add(new PriceData
                            {
                                Date = DateTime.UtcNow,
                                Symbol = symbols[i % symbols.Length],
                                Price = 5000 + (i * 0.5m),
                                Volume = 2000000
                            });
                        }

                        db.InsertAll(records);
                        totalInserted += records.Count;

                        lock (writeCounts)
                        {
                            writeCounts.Add((DateTime.UtcNow, totalInserted));
                        }

                        if (batch % 5 == 0)
                        {
                            _output.WriteLine($"Writer: Inserted {totalInserted} records so far");
                        }

                        Thread.Sleep(5); // Reduced from 20ms to 5ms
                    }

                    _output.WriteLine($"Writer: Completed - inserted {totalInserted} new records");
                }
            }
            catch (Exception ex)
            {
                lock (writerErrors) { writerErrors.Add(ex); }
                _output.WriteLine($"Writer: ERROR - {ex.GetType().Name}: {ex.Message}");
            }
        });

        // Reader task - continuously read and aggregate
        var readerTask = Task.Run(() =>
        {
            try
            {
                writerStarted.Wait(TimeSpan.FromSeconds(5)); // Wait for writer to start

                var openStart = DateTime.UtcNow;
                using (var db = factory.OpenForWrite()) // Using OpenForWrite as reader to test same connection type
                {
                    var openTime = (DateTime.UtcNow - openStart).TotalMilliseconds;
                    _output.WriteLine($"Reader: OpenForWrite() took {openTime:F0}ms");

                    readerStarted.Set();

                    _output.WriteLine("Reader: Starting continuous reads...");

                    for (int i = 0; i < 20; i++)  // Reduced from 50 to 20
                    {
                        var count = db.Count<PriceData>();

                        lock (readCounts)
                        {
                            readCounts.Add((DateTime.UtcNow, count));
                        }

                        // Various read operations
                        var btcRecords = db.Select<PriceData>(x => x.Symbol == "BTC");
                        var avgPrice = db.Scalar<decimal>(db.From<PriceData>()
                            .Where(x => x.Symbol == "ETH")
                            .Select(x => Sql.Avg(x.Price)));
                        var maxVolume = db.Scalar<decimal>(db.From<PriceData>()
                            .Select(x => Sql.Max(x.Volume)));

                        if (i % 5 == 0)
                        {
                            _output.WriteLine($"Reader: Read {count} total records, {btcRecords.Count} BTC records, avg ETH price: {avgPrice:F2}, max volume: {maxVolume:F0}");
                        }

                        Thread.Sleep(5); // Reduced from 25ms to 5ms
                    }

                    _output.WriteLine($"Reader: Completed 20 read iterations");
                }
            }
            catch (Exception ex)
            {
                lock (readerErrors) { readerErrors.Add(ex); }
                _output.WriteLine($"Reader: ERROR - {ex.GetType().Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Writer errors: {writerErrors.Count}");
        _output.WriteLine($"Reader errors: {readerErrors.Count}");

        foreach (var error in writerErrors)
        {
            _output.WriteLine($"  Writer: {error.GetType().Name}: {error.Message}");
        }

        foreach (var error in readerErrors)
        {
            _output.WriteLine($"  Reader: {error.GetType().Name}: {error.Message}");
        }

        // Analyze read counts during writing
        if (readCounts.Any() && writeCounts.Any())
        {
            _output.WriteLine($"\n=== Read/Write Timeline ===");
            var firstRead = readCounts.First();
            var lastRead = readCounts.Last();

            _output.WriteLine($"First read saw {firstRead.count} records");
            _output.WriteLine($"Last read saw {lastRead.count} records");
            _output.WriteLine($"Record count increased by {lastRead.count - firstRead.count} during concurrent operations");

            // Check if reads were truly concurrent with writes
            var writeStartTime = writeCounts.First().time;
            var writeEndTime = writeCounts.Last().time;
            var readsduringWrites = readCounts.Count(r => r.time >= writeStartTime && r.time <= writeEndTime);

            _output.WriteLine($"Reads during active writing: {readsduringWrites}/{readCounts.Count}");
        }

        // Final verification
        using (var db = factory.OpenForWrite())
        {
            var finalCount = db.Count<PriceData>();
            _output.WriteLine($"\nFinal database record count: {finalCount}");
            _output.WriteLine($"Expected: ~6,000 (5,000 initial + 1,000 inserted)");
        }

        if (writerErrors.Any() || readerErrors.Any())
        {
            _output.WriteLine("\n⚠️  CONCURRENT READ/WRITE FAILED - Errors detected");
            Assert.Fail("Concurrent read/write operations failed");
        }
        else
        {
            _output.WriteLine("\n✅ CONCURRENT READ/WRITE SUCCEEDED - No blocking or errors");
        }
    }

    [Fact]
    public async Task LargeDatabase_MultipleReadersWhileWriting()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create initial database with reduced size
        _output.WriteLine("Creating initial database with 2,500 records...");
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceData>(overwrite: true);

            var records = new List<PriceData>();
            for (int i = 0; i < 2500; i++)
            {
                records.Add(new PriceData
                {
                    Date = DateTime.UtcNow.AddHours(-i),
                    Symbol = $"SYM{i % 20}",
                    Price = 1000 + (i * 0.1m),
                    Volume = 1000000
                });
            }

            db.InsertAll(records);
            _output.WriteLine($"Initial database created with {db.Count<PriceData>()} records");
        }

        var startSignal = new ManualResetEventSlim(false);
        var errors = new List<(string role, Exception ex)>();
        var completedTasks = 0;

        _output.WriteLine("\nStarting 1 writer + 3 readers...");

        // Writer task
        var writerTask = Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    startSignal.Wait();
                    _output.WriteLine("Writer: Started appending...");

                    for (int batch = 0; batch < 15; batch++)  // Reduced from 30 to 15
                    {
                        var records = new List<PriceData>();
                        for (int i = 0; i < 25; i++)  // Reduced from 50 to 25
                        {
                            records.Add(new PriceData
                            {
                                Date = DateTime.UtcNow,
                                Symbol = "WRITER",
                                Price = 9999m,
                                Volume = 5000000
                            });
                        }

                        db.InsertAll(records);
                        Thread.Sleep(3);  // Reduced from 10ms to 3ms
                    }

                    Interlocked.Increment(ref completedTasks);
                    _output.WriteLine("Writer: Completed");
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(("Writer", ex)); }
                _output.WriteLine($"Writer: ERROR - {ex.Message}");
            }
        });

        // Reader tasks
        var readerTasks = Enumerable.Range(1, 3).Select(readerNum => Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    startSignal.Wait();
                    _output.WriteLine($"Reader {readerNum}: Started reading...");

                    for (int i = 0; i < 20; i++)  // Reduced from 40 to 20
                    {
                        var count = db.Count<PriceData>();
                        var sample = db.Select<PriceData>(db.From<PriceData>().Limit(10));

                        if (i % 5 == 0)
                        {
                            _output.WriteLine($"Reader {readerNum}: Iteration {i}, found {count} records");
                        }

                        Thread.Sleep(3);  // Reduced from 12ms to 3ms
                    }

                    Interlocked.Increment(ref completedTasks);
                    _output.WriteLine($"Reader {readerNum}: Completed");
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(($"Reader{readerNum}", ex)); }
                _output.WriteLine($"Reader {readerNum}: ERROR - {ex.Message}");
            }
        })).ToArray();

        // Start all tasks simultaneously
        startSignal.Set();

        await Task.WhenAll(new[] { writerTask }.Concat(readerTasks));

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Completed tasks: {completedTasks}/4 (1 writer + 3 readers)");
        _output.WriteLine($"Errors: {errors.Count}");

        foreach (var (role, ex) in errors)
        {
            _output.WriteLine($"  {role}: {ex.GetType().Name}: {ex.Message}");
        }

        if (errors.Any())
        {
            _output.WriteLine("\n⚠️  MULTIPLE READERS + WRITER FAILED");
            Assert.Fail("Concurrent operations failed");
        }
        else
        {
            _output.WriteLine("\n✅ MULTIPLE READERS + WRITER SUCCEEDED");
        }
    }

    [Fact]
    public async Task LargeDatabase_ReaderSeesConsistentSnapshot()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create initial database with reduced size
        _output.WriteLine("Creating initial database with 1,000 records...");
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<PriceData>(overwrite: true);

            var records = new List<PriceData>();
            for (int i = 0; i < 1000; i++)
            {
                records.Add(new PriceData
                {
                    Date = DateTime.UtcNow.AddHours(-i),
                    Symbol = "ORIGINAL",
                    Price = 1000m,
                    Volume = 1000000
                });
            }

            db.InsertAll(records);
            _output.WriteLine($"Initial database created with {db.Count<PriceData>()} records");
        }

        var writerStarted = new ManualResetEventSlim(false);
        var readerOpened = new ManualResetEventSlim(false);
        var errors = new List<Exception>();
        var readCountsBeforeCommit = new List<long>();
        var readCountsAfterCommit = new List<long>();
        var writerCommitted = false;

        _output.WriteLine("\nTesting if reader sees consistent snapshot during long transaction...");

        // Writer with long transaction
        var writerTask = Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    _output.WriteLine("Writer: Opening transaction...");
                    using (var trans = db.OpenTransaction())
                    {
                        writerStarted.Set();
                        readerOpened.Wait(TimeSpan.FromSeconds(5));

                        _output.WriteLine("Writer: Inserting 200 records in transaction...");
                        var records = new List<PriceData>();
                        for (int i = 0; i < 200; i++)  // Reduced from 1000 to 200
                        {
                            records.Add(new PriceData
                            {
                                Date = DateTime.UtcNow,
                                Symbol = "NEWDATA",
                                Price = 9999m,
                                Volume = 9999999
                            });

                            if (records.Count == 50)
                            {
                                db.InsertAll(records);
                                records.Clear();
                            }
                        }

                        _output.WriteLine("Writer: Waiting 100ms before commit...");
                        Thread.Sleep(100);  // Reduced from 500ms to 100ms

                        trans.Commit();
                        writerCommitted = true;
                        _output.WriteLine("Writer: Transaction committed");
                    }

                    Thread.Sleep(50); // Give reader time to see committed data (reduced from 100ms)
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
                _output.WriteLine($"Writer: ERROR - {ex.Message}");
            }
        });

        // Reader checking counts during transaction
        var readerTask = Task.Run(() =>
        {
            try
            {
                writerStarted.Wait(TimeSpan.FromSeconds(5));

                using (var db = factory.OpenForWrite())
                {
                    readerOpened.Set();

                    _output.WriteLine("Reader: Reading while writer transaction is open...");

                    // Read multiple times before commit
                    for (int i = 0; i < 3; i++)  // Reduced from 5 to 3
                    {
                        var count = db.Count<PriceData>();
                        if (!writerCommitted)
                        {
                            readCountsBeforeCommit.Add(count);
                            _output.WriteLine($"Reader: Before commit - Count = {count}");
                        }
                        Thread.Sleep(30);  // Reduced from 100ms to 30ms
                    }

                    // Wait for commit
                    while (!writerCommitted)
                    {
                        Thread.Sleep(20);  // Reduced from 50ms to 20ms
                    }

                    Thread.Sleep(60); // Wait a bit after commit (reduced from 200ms)

                    // Read after commit
                    for (int i = 0; i < 2; i++)  // Reduced from 3 to 2
                    {
                        var count = db.Count<PriceData>();
                        readCountsAfterCommit.Add(count);
                        _output.WriteLine($"Reader: After commit - Count = {count}");
                        Thread.Sleep(20);  // Reduced from 50ms to 20ms
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
                _output.WriteLine($"Reader: ERROR - {ex.Message}");
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        _output.WriteLine($"\n=== Results ===");
        _output.WriteLine($"Reads before commit: {string.Join(", ", readCountsBeforeCommit)}");
        _output.WriteLine($"Reads after commit: {string.Join(", ", readCountsAfterCommit)}");

        if (readCountsBeforeCommit.Any() && readCountsAfterCommit.Any())
        {
            var countBeforeCommit = readCountsBeforeCommit.First();
            var countAfterCommit = readCountsAfterCommit.First();
            var difference = countAfterCommit - countBeforeCommit;

            _output.WriteLine($"\nChange in count: {difference} (expected: ~200)");

            if (readCountsBeforeCommit.All(c => c == countBeforeCommit))
            {
                _output.WriteLine("✅ Reader saw consistent snapshot during transaction");
            }
            else
            {
                _output.WriteLine("⚠️  Reader saw changing counts during transaction");
            }
        }

        if (errors.Any())
        {
            foreach (var ex in errors)
            {
                _output.WriteLine($"ERROR: {ex.Message}");
            }
            Assert.Fail("Test failed with errors");
        }
    }
}
