using System;
using System.Linq;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.DuckDb;
using Xunit;

namespace DuckDbOrmLite.Tests;

/// <summary>
/// Advanced feature tests for production readiness
/// Tests JOINs, aggregations, error handling, and edge cases
/// </summary>
[Collection("DuckDB Tests")]
public class AdvancedFeatureTests : IDisposable
{
    private readonly OrmLiteConnectionFactory _dbFactory;

    public AdvancedFeatureTests(TestFixture fixture)
    {
        _dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");
        // BeforeExecFilter is set up globally by TestFixture
    }

    public void Dispose()
    {
        // Nothing to dispose
    }

    #region JOIN Tests

    [Fact]
    public void Can_Perform_Inner_Join()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Customer>(overwrite: true);
        db.CreateTable<Order>(overwrite: true);

        // Insert test data
        db.Insert(new Customer { Id = 1, Name = "Alice", Email = "alice@test.com" });
        db.Insert(new Customer { Id = 2, Name = "Bob", Email = "bob@test.com" });
        db.Insert(new Order { Id = 1, CustomerId = 1, OrderNumber = "ORD-001", TotalAmount = 100m });
        db.Insert(new Order { Id = 2, CustomerId = 1, OrderNumber = "ORD-002", TotalAmount = 200m });

        // Perform JOIN
        var q = db.From<Order>()
            .Join<Customer>((o, c) => o.CustomerId == c.Id)
            .Where<Order>(o => o.CustomerId == 1);

        var results = db.Select(q);

        Assert.Equal(2, results.Count);
        Assert.All(results, o => Assert.Equal(1, o.CustomerId));
    }

    [Fact]
    public void Can_Select_From_Join_With_Custom_Fields()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Customer>(overwrite: true);
        db.CreateTable<Order>(overwrite: true);

        db.Insert(new Customer { Id = 1, Name = "Alice", Email = "alice@test.com" });
        db.Insert(new Order { Id = 1, CustomerId = 1, OrderNumber = "ORD-001", TotalAmount = 100m });

        // Select specific fields from JOIN
        var result = db.Single<Order>(db.From<Order>()
            .Join<Customer>((o, c) => o.CustomerId == c.Id)
            .Where<Customer>(c => c.Name == "Alice"));

        Assert.NotNull(result);
        Assert.Equal("ORD-001", result.OrderNumber);
    }

    #endregion

    #region Aggregation Tests

    [Fact]
    public void Can_Use_Count_Aggregate()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.Insert(new Product { Id = 1, Name = "Laptop", Category = "Electronics", Price = 1000m });
        db.Insert(new Product { Id = 2, Name = "Mouse", Category = "Electronics", Price = 25m });
        db.Insert(new Product { Id = 3, Name = "Desk", Category = "Furniture", Price = 500m });

        var count = db.Count<Product>(x => x.Category == "Electronics");

        Assert.Equal(2, count);
    }

    [Fact]
    public void Can_Use_Sum_Aggregate()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Order>(overwrite: true);

        db.Insert(new Order { Id = 1, CustomerId = 1, OrderNumber = "ORD-001", TotalAmount = 100m });
        db.Insert(new Order { Id = 2, CustomerId = 1, OrderNumber = "ORD-002", TotalAmount = 200m });
        db.Insert(new Order { Id = 3, CustomerId = 2, OrderNumber = "ORD-003", TotalAmount = 150m });

        var total = db.Scalar<decimal>(
            db.From<Order>()
                .Where(x => x.CustomerId == 1)
                .Select(x => Sql.Sum(x.TotalAmount))
        );

        Assert.Equal(300m, total);
    }

    [Fact]
    public void Can_Use_Average_Aggregate()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.Insert(new Product { Id = 1, Name = "Item1", Category = "Test", Price = 100m });
        db.Insert(new Product { Id = 2, Name = "Item2", Category = "Test", Price = 200m });
        db.Insert(new Product { Id = 3, Name = "Item3", Category = "Test", Price = 300m });

        var avg = db.Scalar<decimal>(
            db.From<Product>()
                .Select(x => Sql.Avg(x.Price))
        );

        Assert.Equal(200m, avg);
    }

    [Fact]
    public void Can_Use_Min_And_Max_Aggregates()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.Insert(new Product { Id = 1, Name = "Cheap", Category = "Test", Price = 10m });
        db.Insert(new Product { Id = 2, Name = "Medium", Category = "Test", Price = 50m });
        db.Insert(new Product { Id = 3, Name = "Expensive", Category = "Test", Price = 100m });

        var min = db.Scalar<decimal>(db.From<Product>().Select(x => Sql.Min(x.Price)));
        var max = db.Scalar<decimal>(db.From<Product>().Select(x => Sql.Max(x.Price)));

        Assert.Equal(10m, min);
        Assert.Equal(100m, max);
    }

    #endregion

    #region DISTINCT Tests

    [Fact]
    public void Can_Select_Distinct_Values()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.Insert(new Product { Id = 1, Name = "Item1", Category = "Electronics", Price = 100m });
        db.Insert(new Product { Id = 2, Name = "Item2", Category = "Electronics", Price = 200m });
        db.Insert(new Product { Id = 3, Name = "Item3", Category = "Furniture", Price = 300m });
        db.Insert(new Product { Id = 4, Name = "Item4", Category = "Electronics", Price = 150m });

        var categories = db.Column<string>(
            db.From<Product>()
                .SelectDistinct(x => x.Category)
        );

        Assert.Equal(2, categories.Count);
        Assert.Contains("Electronics", categories);
        Assert.Contains("Furniture", categories);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Can_Distinguish_Empty_String_From_Null()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Customer>(overwrite: true);

        db.Insert(new Customer { Id = 1, Name = "Test", Email = "" });
        db.Insert(new Customer { Id = 2, Name = "Test2", Email = null });

        var emptyEmail = db.Single<Customer>(x => x.Email == "");
        var nullEmail = db.Single<Customer>(x => x.Email == null);

        Assert.Equal(1, emptyEmail.Id);
        Assert.Equal("", emptyEmail.Email);
        Assert.Equal(2, nullEmail.Id);
        Assert.Null(nullEmail.Email);
    }

    [Fact]
    public void Can_Handle_Special_Characters_In_Strings()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        var specialName = "Test's \"quoted\" value with $symbols & <tags>";
        db.Insert(new Product { Id = 1, Name = specialName, Category = "Test", Price = 10m });

        var result = db.SingleById<Product>(1);

        Assert.Equal(specialName, result.Name);
    }

    [Fact]
    public void Can_Handle_Large_Decimal_Values()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Order>(overwrite: true);

        // Use a large value that fits within DECIMAL(18,12): max ~999999.999999999999
        var largeAmount = 999999.999999m;
        db.Insert(new Order { Id = 1, CustomerId = 1, OrderNumber = "BIG", TotalAmount = largeAmount });

        var result = db.SingleById<Order>(1);

        Assert.Equal(largeAmount, result.TotalAmount);
    }

    [Fact]
    public void Can_Handle_Very_Long_Strings()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        var longString = new string('x', 10000);
        db.Insert(new Product { Id = 1, Name = longString, Category = "Test", Price = 10m });

        var result = db.SingleById<Product>(1);

        Assert.Equal(10000, result.Name.Length);
        Assert.Equal(longString, result.Name);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Throws_On_Duplicate_Primary_Key()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.Insert(new Product { Id = 1, Name = "Test", Category = "Test", Price = 10m });

        // DuckDB throws DuckDBException for constraint violations
        var ex = Assert.ThrowsAny<Exception>(() =>
            db.Insert(new Product { Id = 1, Name = "Duplicate", Category = "Test", Price = 20m })
        );

        // Verify it's a constraint/duplicate key error
        Assert.Contains("PRIMARY KEY", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prevents_SQL_Injection_In_Parameters()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        var maliciousInput = "'; DROP TABLE Product; --";
        db.Insert(new Product { Id = 1, Name = maliciousInput, Category = "Test", Price = 10m });

        var result = db.Single<Product>(x => x.Name == maliciousInput);

        Assert.Equal(maliciousInput, result.Name);

        // Verify table still exists
        Assert.True(db.TableExists<Product>());
    }

    #endregion

    #region Schema Operations

    [Fact]
    public void Can_Drop_Table()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);
        Assert.True(db.TableExists<Product>());

        db.DropTable<Product>();
        Assert.False(db.TableExists<Product>());
    }

    [Fact]
    public void Can_Recreate_Dropped_Table()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);
        db.Insert(new Product { Id = 1, Name = "Test", Category = "Test", Price = 10m });

        db.DropTable<Product>();
        db.CreateTable<Product>();

        var count = db.Count<Product>();
        Assert.Equal(0, count);
    }

    #endregion

    #region Test Models

    private class Customer
    {
        [PrimaryKey]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = null!;

        public string? Email { get; set; }
    }

    private class Order
    {
        [PrimaryKey]
        public int Id { get; set; }

        public int CustomerId { get; set; }

        public string OrderNumber { get; set; } = null!;

        public decimal TotalAmount { get; set; }
    }

    private class Product
    {
        [PrimaryKey]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = null!;

        public string Category { get; set; } = null!;

        public decimal Price { get; set; }
    }

    #endregion
}
