using System;
using System.Numerics;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.DuckDb;
using Xunit;

namespace DuckDbOrmLite.Tests;

public class DuckDbOrmLiteTests : IDisposable
{
    private readonly OrmLiteConnectionFactory _dbFactory;

    public DuckDbOrmLiteTests()
    {
        // Use in-memory database for tests
        _dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");

        // Enable SQL logging
        // DuckDB.NET supports named parameters with $name syntax natively
        // No special parameter handling needed!
        OrmLiteConfig.BeforeExecFilter = dbCmd => Console.WriteLine(dbCmd.GetDebugString());
    }

    public void Dispose()
    {
        // OrmLiteConnectionFactory doesn't implement IDisposable in this version
    }

    [Fact]
    public void Can_Create_Connection()
    {
        using var db = _dbFactory.Open();
        Assert.NotNull(db);
    }

    [Fact]
    public void Can_Create_Table_With_Basic_Types()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>();

        var tableExists = db.TableExists<BasicTypeTest>();
        Assert.True(tableExists);
    }

    [Fact]
    public void Can_Insert_And_Select_Basic_Types()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var entity = new BasicTypeTest
        {
            Id = 1,  // Explicitly set ID
            Name = "Test",
            Age = 25,
            IsActive = true,
            Balance = 100.50m,
            Height = 5.9f,
            Weight = 180.5,
            CreatedAt = DateTime.UtcNow,
            UserId = Guid.NewGuid()
        };

        db.Insert(entity);

        var retrieved = db.SingleById<BasicTypeTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(entity.Name, retrieved.Name);
        Assert.Equal(entity.Age, retrieved.Age);
        Assert.Equal(entity.IsActive, retrieved.IsActive);
        Assert.Equal(entity.Balance, retrieved.Balance);
        Assert.Equal(entity.Height, retrieved.Height);
        Assert.Equal(entity.Weight, retrieved.Weight);
        Assert.Equal(entity.UserId, retrieved.UserId);
    }

    [Fact]
    public void Can_Update_Record()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var entity = new BasicTypeTest { Id = 1, Name = "Original", Age = 25 };
        db.Insert(entity);

        entity.Name = "Updated";
        entity.Age = 30;
        db.Update(entity);

        var retrieved = db.SingleById<BasicTypeTest>(1);

        Assert.Equal("Updated", retrieved.Name);
        Assert.Equal(30, retrieved.Age);
    }

    [Fact]
    public void Can_Delete_Record()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var entity = new BasicTypeTest { Id = 1, Name = "ToDelete", Age = 25 };
        db.Insert(entity);

        db.DeleteById<BasicTypeTest>(1);

        var retrieved = db.SingleById<BasicTypeTest>(1);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Can_Query_With_Where_Clause()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        db.Insert(new BasicTypeTest { Id = 1, Name = "John", Age = 25 });
        db.Insert(new BasicTypeTest { Id = 2, Name = "Jane", Age = 30 });
        db.Insert(new BasicTypeTest { Id = 3, Name = "Bob", Age = 35 });

        var results = db.Select<BasicTypeTest>(x => x.Age > 25);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Age > 25));
    }

    [Fact]
    public void Can_Use_Parameterized_Query()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        db.Insert(new BasicTypeTest { Id = 1, Name = "John", Age = 25 });
        db.Insert(new BasicTypeTest { Id = 2, Name = "Jane", Age = 30 });

        var results = db.Select<BasicTypeTest>("Age > $1", 25);

        Assert.Single(results);
        Assert.Equal("Jane", results[0].Name);
    }

    [Fact]
    public void Can_Handle_Guid_Type()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var guid = Guid.NewGuid();
        var entity = new BasicTypeTest { Id = 1, Name = "Test", UserId = guid };

        db.Insert(entity);

        var retrieved = db.Single<BasicTypeTest>(x => x.UserId == guid);

        Assert.NotNull(retrieved);
        Assert.Equal(guid, retrieved.UserId);
    }

    [Fact]
    public void Can_Handle_DateTime_Type()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var now = DateTime.UtcNow;
        var entity = new BasicTypeTest { Id = 1, Name = "Test", CreatedAt = now };

        db.Insert(entity);

        var retrieved = db.SingleById<BasicTypeTest>(1);

        Assert.NotNull(retrieved);
        // Allow small difference due to precision
        Assert.True(Math.Abs((retrieved.CreatedAt - now).TotalSeconds) < 1);
    }

    [Fact]
    public void Can_Handle_Decimal_Type()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        var entity = new BasicTypeTest { Id = 1, Name = "Test", Balance = 12345.678901m };

        db.Insert(entity);

        var retrieved = db.SingleById<BasicTypeTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(entity.Balance, retrieved.Balance);
    }

    [Fact]
    public void Can_Handle_ByteArray_Type()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BlobTest>(overwrite: true);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var entity = new BlobTest { Id = 1, Name = "Test", Data = data };

        db.Insert(entity);

        var retrieved = db.SingleById<BlobTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(data, retrieved.Data);
    }

    [Fact]
    public void Can_Use_OrderBy()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        db.Insert(new BasicTypeTest { Id = 1, Name = "Charlie", Age = 30 });
        db.Insert(new BasicTypeTest { Id = 2, Name = "Alice", Age = 25 });
        db.Insert(new BasicTypeTest { Id = 3, Name = "Bob", Age = 35 });

        var results = db.Select(db.From<BasicTypeTest>().OrderBy(x => x.Name));

        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal("Charlie", results[2].Name);
    }

    [Fact]
    public void Can_Use_Limit_And_Offset()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        for (int i = 1; i <= 10; i++)
        {
            db.Insert(new BasicTypeTest { Id = i, Name = $"User{i}", Age = i });
        }

        var results = db.Select(db.From<BasicTypeTest>().OrderBy(x => x.Age).Limit(5).Skip(3));

        Assert.Equal(5, results.Count);
        Assert.Equal("User4", results[0].Name);
        Assert.Equal("User8", results[4].Name);
    }

    [Fact]
    public void Can_Count_Records()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        db.Insert(new BasicTypeTest { Id = 1, Name = "User1", Age = 25 });
        db.Insert(new BasicTypeTest { Id = 2, Name = "User2", Age = 30 });
        db.Insert(new BasicTypeTest { Id = 3, Name = "User3", Age = 35 });

        var count = db.Count<BasicTypeTest>(x => x.Age > 25);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Can_Handle_All_Integer_Types()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<IntegerTypeTest>(overwrite: true);

        var entity = new IntegerTypeTest
        {
            Id = 1,
            TinyInt = 127,
            UTinyInt = 255,
            SmallInt = 32767,
            USmallInt = 65535,
            Integer = 2147483647,
            UInteger = 4294967295,
            BigInt = 9223372036854775807L,
            UBigInt = 18446744073709551615UL
        };

        db.Insert(entity);

        var retrieved = db.SingleById<IntegerTypeTest>(1);

        Assert.NotNull(retrieved);
        Assert.Equal(entity.TinyInt, retrieved.TinyInt);
        Assert.Equal(entity.UTinyInt, retrieved.UTinyInt);
        Assert.Equal(entity.SmallInt, retrieved.SmallInt);
        Assert.Equal(entity.USmallInt, retrieved.USmallInt);
        Assert.Equal(entity.Integer, retrieved.Integer);
        Assert.Equal(entity.UInteger, retrieved.UInteger);
        Assert.Equal(entity.BigInt, retrieved.BigInt);
        Assert.Equal(entity.UBigInt, retrieved.UBigInt);
    }

    [Fact]
    public void Can_Check_Table_Exists()
    {
        using var db = _dbFactory.Open();

        db.DropTable<BasicTypeTest>();
        Assert.False(db.TableExists<BasicTypeTest>());

        db.CreateTable<BasicTypeTest>();
        Assert.True(db.TableExists<BasicTypeTest>());
    }

    [Fact]
    public void Can_Handle_Null_Values()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<NullableTest>(overwrite: true);

        var entity = new NullableTest
        {
            Id = 1,
            Name = "Test",
            Age = null,
            IsActive = null,
            CreatedAt = null
        };

        db.Insert(entity);

        var retrieved = db.SingleById<NullableTest>(1);

        Assert.NotNull(retrieved);
        Assert.Null(retrieved.Age);
        Assert.Null(retrieved.IsActive);
        Assert.Null(retrieved.CreatedAt);
    }

    [Fact]
    public void Can_Use_Complex_Where_Expression()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<BasicTypeTest>(overwrite: true);

        db.Insert(new BasicTypeTest { Id = 1, Name = "John", Age = 25, IsActive = true });
        db.Insert(new BasicTypeTest { Id = 2, Name = "Jane", Age = 30, IsActive = false });
        db.Insert(new BasicTypeTest { Id = 3, Name = "Bob", Age = 35, IsActive = true });

        var results = db.Select<BasicTypeTest>(x => x.Age > 25 && x.IsActive);

        Assert.Single(results);
        Assert.Equal("Bob", results[0].Name);
    }
}

// Test Models
public class BasicTypeTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Name { get; set; }
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public float Height { get; set; }
    public double Weight { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid UserId { get; set; }
}

public class BlobTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Name { get; set; }
    public byte[] Data { get; set; }
}

public class IntegerTypeTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public sbyte TinyInt { get; set; }
    public byte UTinyInt { get; set; }
    public short SmallInt { get; set; }
    public ushort USmallInt { get; set; }
    public int Integer { get; set; }
    public uint UInteger { get; set; }
    public long BigInt { get; set; }
    public ulong UBigInt { get; set; }
}

public class NullableTest
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Name { get; set; }
    public int? Age { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
}
