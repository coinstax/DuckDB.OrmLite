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

public class ConcurrencyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public class TestRecord
    {
        [AutoIncrement]
        public int Id { get; set; }
        public int ThreadId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public ConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"concurrency_test_{Guid.NewGuid()}.db");
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
    public void SingleThread_MultipleOpenForWrite_Sequential()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create table
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<TestRecord>(overwrite: true);
        }

        // Open for write multiple times sequentially - should always work
        using (var db1 = factory.OpenForWrite())
        {
            db1.Insert(new TestRecord { ThreadId = 1, Timestamp = DateTime.UtcNow, Data = "First" });
        }

        using (var db2 = factory.OpenForWrite())
        {
            db2.Insert(new TestRecord { ThreadId = 2, Timestamp = DateTime.UtcNow, Data = "Second" });
        }

        using (var db3 = factory.OpenForWrite())
        {
            var count = db3.Count<TestRecord>();
            Assert.Equal(2, count);
        }

        _output.WriteLine("✅ Sequential OpenForWrite works fine");
    }

    [Fact]
    public async Task MultipleThreads_ConcurrentOpenForWrite_Inserts()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create table
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<TestRecord>(overwrite: true);
        }

        var errors = new List<Exception>();
        var successCount = 0;
        var threads = 5;
        var insertsPerThread = 10;

        _output.WriteLine($"Starting {threads} threads, each inserting {insertsPerThread} records...");

        var tasks = Enumerable.Range(0, threads).Select(threadNum => Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    _output.WriteLine($"Thread {threadNum}: OpenForWrite() succeeded");

                    for (int i = 0; i < insertsPerThread; i++)
                    {
                        db.Insert(new TestRecord
                        {
                            ThreadId = threadNum,
                            Timestamp = DateTime.UtcNow,
                            Data = $"Thread{threadNum}_Record{i}"
                        });

                        // Small delay to increase chance of concurrent writes
                        Thread.Sleep(1);
                    }

                    Interlocked.Increment(ref successCount);
                    _output.WriteLine($"Thread {threadNum}: Completed {insertsPerThread} inserts");
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

        // Verify data
        using (var db = factory.OpenForWrite())
        {
            var totalRecords = db.Count<TestRecord>();
            _output.WriteLine($"Total records in database: {totalRecords}");

            var recordsByThread = db.Select<TestRecord>()
                .GroupBy(r => r.ThreadId)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var group in recordsByThread)
            {
                _output.WriteLine($"  Thread {group.Key}: {group.Count()} records");
            }
        }

        // The key question: Did concurrent OpenForWrite() calls succeed or fail?
        if (errors.Any())
        {
            _output.WriteLine("\n⚠️  CONCURRENT WRITES FAILED - Only one writer allowed");
        }
        else
        {
            _output.WriteLine("\n✅ CONCURRENT WRITES SUCCEEDED - Multiple writers allowed in same process");
        }
    }

    [Fact]
    public async Task MultipleThreads_OneOpenForWrite_OneOpen_ShouldNotConflict()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create table
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<TestRecord>(overwrite: true);
            db.Insert(new TestRecord { ThreadId = 0, Timestamp = DateTime.UtcNow, Data = "Initial" });
        }

        var errors = new List<Exception>();

        _output.WriteLine("Starting concurrent reader and writer...");

        var writerTask = Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    _output.WriteLine("Writer: OpenForWrite() succeeded");

                    for (int i = 0; i < 20; i++)
                    {
                        db.Insert(new TestRecord
                        {
                            ThreadId = 1,
                            Timestamp = DateTime.UtcNow,
                            Data = $"Writer_Record{i}"
                        });
                        Thread.Sleep(10);
                    }

                    _output.WriteLine("Writer: Completed 20 inserts");
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
                _output.WriteLine($"Writer: ERROR - {ex.Message}");
            }
        });

        var readerTask = Task.Run(() =>
        {
            try
            {
                Thread.Sleep(50); // Let writer start first

                using (var db = factory.OpenForWrite()) // Using OpenForWrite as reader
                {
                    _output.WriteLine("Reader: OpenForWrite() succeeded");

                    for (int i = 0; i < 10; i++)
                    {
                        var count = db.Count<TestRecord>();
                        _output.WriteLine($"Reader: Found {count} records");
                        Thread.Sleep(20);
                    }

                    _output.WriteLine("Reader: Completed reads");
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
        _output.WriteLine($"Errors: {errors.Count}");

        foreach (var error in errors)
        {
            _output.WriteLine($"  - {error.GetType().Name}: {error.Message}");
        }

        if (errors.Any())
        {
            _output.WriteLine("\n⚠️  CONCURRENT READ/WRITE FAILED");
        }
        else
        {
            _output.WriteLine("\n✅ CONCURRENT READ/WRITE SUCCEEDED");
        }
    }

    [Fact]
    public async Task LongRunningWrite_BlocksOtherWrites()
    {
        var factory = new DuckDbOrmLiteConnectionFactory($"Data Source={_testDbPath}");

        // Create table
        using (var db = factory.OpenForWrite())
        {
            db.CreateTable<TestRecord>(overwrite: true);
        }

        var thread1Started = new ManualResetEventSlim(false);
        var thread2AttemptTime = DateTime.MinValue;
        var thread2SuccessTime = DateTime.MinValue;
        var errors = new List<Exception>();

        _output.WriteLine("Starting long-running write in Thread 1...");

        var thread1 = Task.Run(() =>
        {
            try
            {
                using (var db = factory.OpenForWrite())
                {
                    thread1Started.Set();
                    _output.WriteLine("Thread 1: Started long transaction");

                    using (var trans = db.OpenTransaction())
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            db.Insert(new TestRecord
                            {
                                ThreadId = 1,
                                Timestamp = DateTime.UtcNow,
                                Data = $"Thread1_Record{i}"
                            });
                            Thread.Sleep(100); // Slow write
                        }

                        trans.Commit();
                    }

                    _output.WriteLine("Thread 1: Transaction committed");
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
                _output.WriteLine($"Thread 1: ERROR - {ex.Message}");
            }
        });

        var thread2 = Task.Run(() =>
        {
            try
            {
                thread1Started.Wait(); // Wait for thread 1 to start
                Thread.Sleep(200); // Let thread 1 get into transaction

                thread2AttemptTime = DateTime.UtcNow;
                _output.WriteLine("Thread 2: Attempting OpenForWrite()...");

                using (var db = factory.OpenForWrite())
                {
                    thread2SuccessTime = DateTime.UtcNow;
                    var waitTime = (thread2SuccessTime - thread2AttemptTime).TotalMilliseconds;

                    _output.WriteLine($"Thread 2: OpenForWrite() succeeded after {waitTime:F0}ms");

                    db.Insert(new TestRecord
                    {
                        ThreadId = 2,
                        Timestamp = DateTime.UtcNow,
                        Data = "Thread2_Record"
                    });
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
                _output.WriteLine($"Thread 2: ERROR - {ex.Message}");
            }
        });

        await Task.WhenAll(thread1, thread2);

        _output.WriteLine($"\n=== Results ===");

        if (thread2SuccessTime != DateTime.MinValue)
        {
            var waitTime = (thread2SuccessTime - thread2AttemptTime).TotalMilliseconds;

            if (waitTime > 500)
            {
                _output.WriteLine($"⚠️  Thread 2 WAITED {waitTime:F0}ms - writes are serialized");
            }
            else
            {
                _output.WriteLine($"✅ Thread 2 succeeded immediately ({waitTime:F0}ms) - concurrent writes allowed");
            }
        }

        _output.WriteLine($"Errors: {errors.Count}");
        foreach (var error in errors)
        {
            _output.WriteLine($"  - {error.GetType().Name}: {error.Message}");
        }
    }
}
