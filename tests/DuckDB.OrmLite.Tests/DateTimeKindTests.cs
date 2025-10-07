using System;
using System.IO;
using DuckDB.OrmLite;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using Xunit;
using Xunit.Abstractions;

namespace DuckDbOrmLite.Tests;

[Collection("DuckDB Tests")]
public class DateTimeKindTests
{
    private readonly ITestOutputHelper _output;
    private readonly DuckDbOrmLiteConnectionFactory _dbFactory;

    public DateTimeKindTests(ITestOutputHelper output)
    {
        _output = output;
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_datetime_kind_{Guid.NewGuid()}.db");
        _dbFactory = new DuckDbOrmLiteConnectionFactory($"Data Source={dbPath}");
    }

    [Fact]
    public void UtcDateTime_PreservesUtcKind()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var utcNow = DateTime.UtcNow;
        var entity = new DateTimeTest { Id = 1, Timestamp = utcNow };

        db.Insert(entity);
        var retrieved = db.SingleById<DateTimeTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(DateTimeKind.Utc, retrieved.Timestamp.Kind);
        _output.WriteLine($"Stored: {utcNow:O} (Kind: {utcNow.Kind})");
        _output.WriteLine($"Retrieved: {retrieved.Timestamp:O} (Kind: {retrieved.Timestamp.Kind})");
    }

    [Fact]
    public void UnspecifiedDateTime_ReturnsAsUtc()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        // Create DateTime with Unspecified kind
        var unspecifiedDateTime = new DateTime(2025, 10, 6, 12, 30, 45, DateTimeKind.Unspecified);
        var entity = new DateTimeTest { Id = 1, Timestamp = unspecifiedDateTime };

        db.Insert(entity);
        var retrieved = db.SingleById<DateTimeTest>(1);

        Assert.NotNull(retrieved);
        // Should come back as UTC (treating Unspecified as UTC)
        Assert.Equal(DateTimeKind.Utc, retrieved.Timestamp.Kind);
        // Values should match (since we treat Unspecified as UTC)
        Assert.Equal(unspecifiedDateTime.Year, retrieved.Timestamp.Year);
        Assert.Equal(unspecifiedDateTime.Month, retrieved.Timestamp.Month);
        Assert.Equal(unspecifiedDateTime.Day, retrieved.Timestamp.Day);
        Assert.Equal(unspecifiedDateTime.Hour, retrieved.Timestamp.Hour);
        Assert.Equal(unspecifiedDateTime.Minute, retrieved.Timestamp.Minute);
        Assert.Equal(unspecifiedDateTime.Second, retrieved.Timestamp.Second);

        _output.WriteLine($"Stored: {unspecifiedDateTime:O} (Kind: {unspecifiedDateTime.Kind})");
        _output.WriteLine($"Retrieved: {retrieved.Timestamp:O} (Kind: {retrieved.Timestamp.Kind})");
    }

    [Fact]
    public void LocalDateTime_ConvertedToUtc()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var localNow = DateTime.Now; // Local time
        var entity = new DateTimeTest { Id = 1, Timestamp = localNow };

        db.Insert(entity);
        var retrieved = db.SingleById<DateTimeTest>(1);

        Assert.NotNull(retrieved);
        // Should come back as UTC
        Assert.Equal(DateTimeKind.Utc, retrieved.Timestamp.Kind);

        // The UTC equivalent should match
        var expectedUtc = localNow.ToUniversalTime();
        Assert.True(Math.Abs((retrieved.Timestamp - expectedUtc).TotalSeconds) < 1);

        _output.WriteLine($"Stored (Local): {localNow:O} (Kind: {localNow.Kind})");
        _output.WriteLine($"Expected UTC: {expectedUtc:O}");
        _output.WriteLine($"Retrieved: {retrieved.Timestamp:O} (Kind: {retrieved.Timestamp.Kind})");
    }

    [Fact]
    public void MultipleRecords_AllReturnUtcKind()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var records = new[]
        {
            new DateTimeTest { Id = 1, Timestamp = DateTime.UtcNow },
            new DateTimeTest { Id = 2, Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) },
            new DateTimeTest { Id = 3, Timestamp = DateTime.Now }
        };

        db.InsertAll(records);
        var retrieved = db.Select<DateTimeTest>();

        Assert.Equal(3, retrieved.Count);
        foreach (var record in retrieved)
        {
            Assert.Equal(DateTimeKind.Utc, record.Timestamp.Kind);
            _output.WriteLine($"Id {record.Id}: {record.Timestamp:O} (Kind: {record.Timestamp.Kind})");
        }
    }

    [Fact]
    public void QueryWithDateTime_WorksCorrectly()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var baseTime = new DateTime(2025, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        db.InsertAll(new[]
        {
            new DateTimeTest { Id = 1, Timestamp = baseTime.AddHours(-1) },
            new DateTimeTest { Id = 2, Timestamp = baseTime },
            new DateTimeTest { Id = 3, Timestamp = baseTime.AddHours(1) }
        });

        // Query with UTC DateTime
        var results = db.Select<DateTimeTest>(x => x.Timestamp >= baseTime);

        Assert.Equal(2, results.Count);
        foreach (var result in results)
        {
            Assert.Equal(DateTimeKind.Utc, result.Timestamp.Kind);
            _output.WriteLine($"Found: Id {result.Id}, Time: {result.Timestamp:O}");
        }
    }

    [Fact]
    public void NullableDateTime_PreservesUtcKind()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<NullableDateTimeTest>(overwrite: true);

        var utcNow = DateTime.UtcNow;
        db.InsertAll(new[]
        {
            new NullableDateTimeTest { Id = 1, Timestamp = utcNow },
            new NullableDateTimeTest { Id = 2, Timestamp = null }
        });

        var withValue = db.SingleById<NullableDateTimeTest>(1);
        var withNull = db.SingleById<NullableDateTimeTest>(2);

        Assert.NotNull(withValue.Timestamp);
        Assert.Equal(DateTimeKind.Utc, withValue.Timestamp.Value.Kind);
        Assert.Null(withNull.Timestamp);

        _output.WriteLine($"With value: {withValue.Timestamp:O} (Kind: {withValue.Timestamp.Value.Kind})");
        _output.WriteLine($"With null: {withNull.Timestamp?.ToString("O") ?? "NULL"}");
    }

    [Fact]
    public void BulkInsert_PreservesUtcKind()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var records = new[]
        {
            new DateTimeTest { Id = 1, Timestamp = DateTime.UtcNow },
            new DateTimeTest { Id = 2, Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) },
            new DateTimeTest { Id = 3, Timestamp = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc) }
        };

        db.BulkInsert(records);
        var retrieved = db.Select<DateTimeTest>();

        Assert.Equal(3, retrieved.Count);
        foreach (var record in retrieved)
        {
            Assert.Equal(DateTimeKind.Utc, record.Timestamp.Kind);
            _output.WriteLine($"BulkInserted Id {record.Id}: {record.Timestamp:O} (Kind: {record.Timestamp.Kind})");
        }
    }

    [Fact]
    public void UpdateDateTime_PreservesUtcKind()
    {
        using var db = _dbFactory.Open();
        db.CreateTable<DateTimeTest>(overwrite: true);

        var original = new DateTimeTest { Id = 1, Timestamp = DateTime.UtcNow };
        db.Insert(original);

        // Update with Unspecified DateTime
        var newTime = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Unspecified);
        original.Timestamp = newTime;
        db.Update(original);

        var retrieved = db.SingleById<DateTimeTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(DateTimeKind.Utc, retrieved.Timestamp.Kind);
        Assert.Equal(newTime.Year, retrieved.Timestamp.Year);
        Assert.Equal(newTime.Month, retrieved.Timestamp.Month);
        Assert.Equal(newTime.Day, retrieved.Timestamp.Day);

        _output.WriteLine($"Updated with: {newTime:O} (Kind: {newTime.Kind})");
        _output.WriteLine($"Retrieved: {retrieved.Timestamp:O} (Kind: {retrieved.Timestamp.Kind})");
    }
}

public class DateTimeTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public DateTime Timestamp { get; set; }
}

public class NullableDateTimeTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public DateTime? Timestamp { get; set; }
}
