using System;
using System.Collections.Generic;
using ServiceStack.DataAnnotations;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.DuckDb;
using Xunit;
using Xunit.Abstractions;

namespace DuckDbOrmLite.Tests;

/// <summary>
/// Integration tests demonstrating real-world usage scenarios
/// </summary>
public class ExampleUsageTests : IDisposable
{
    private readonly OrmLiteConnectionFactory _dbFactory;
    private readonly ITestOutputHelper _output;

    public ExampleUsageTests(ITestOutputHelper output)
    {
        _output = output;
        // Use in-memory database for examples
        _dbFactory = new DuckDbOrmLiteConnectionFactory("Data Source=:memory:");

        // Fix 0-based positional parameters to 1-based for DuckDB
        OrmLiteConfig.BeforeExecFilter = dbCmd =>
        {
            // DuckDB uses 1-based positional parameters ($1, $2), but OrmLite uses 0-based ($0, $1)
            // Convert SQL: $0 -> $1, $1 -> $2, etc.
            var sql = dbCmd.CommandText;
            for (int i = 9; i >= 0; i--)
            {
                sql = sql.Replace($"${i}", $"${i + 1}");
            }
            dbCmd.CommandText = sql;

            // Strip $ from ALL parameter names for DuckDB.NET (it expects names without $ but SQL with $)
            foreach (System.Data.IDbDataParameter param in dbCmd.Parameters)
            {
                if (param.ParameterName.StartsWith("$"))
                {
                    param.ParameterName = param.ParameterName.Substring(1);
                }
            }
        };
    }

    public void Dispose()
    {
        // OrmLiteConnectionFactory doesn't implement IDisposable in this version
    }

    [Fact]
    public void Example_Complete_CRUD_Operations()
    {
        using var db = _dbFactory.Open();

        // Create table
        db.CreateTable<Customer>(overwrite: true);
        _output.WriteLine("✓ Table created");

        // Insert
        var customer = new Customer
        {
            Id = 1,
            CustomerId = Guid.NewGuid(),
            Name = "Acme Corporation",
            Email = "contact@acme.com",
            CreditLimit = 50000.00m,
            IsActive = true,
            RegisteredAt = DateTime.UtcNow
        };

        db.Insert(customer);
        _output.WriteLine($"✓ Inserted customer: {customer.Name} (ID: {customer.Id})");

        // Select by ID
        var retrieved = db.SingleById<Customer>(1);
        Assert.Equal(customer.Name, retrieved.Name);
        _output.WriteLine($"✓ Retrieved customer: {retrieved.Name}");

        // Update
        retrieved.CreditLimit = 75000.00m;
        db.Update(retrieved);
        _output.WriteLine($"✓ Updated credit limit to {retrieved.CreditLimit:C}");

        // Verify update
        var updated = db.SingleById<Customer>(1);
        Assert.Equal(75000.00m, updated.CreditLimit);

        // Delete
        db.DeleteById<Customer>(1);
        _output.WriteLine("✓ Deleted customer");

        // Verify deletion
        var deleted = db.SingleById<Customer>(1);
        Assert.Null(deleted);
        _output.WriteLine("✓ Verified deletion");
    }

    [Fact]
    public void Example_Querying_With_Linq()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        // Insert sample data
        var products = new List<Product>
        {
            new() { Id = 1, Name = "Laptop", Category = "Electronics", Price = 1299.99m, Stock = 50 },
            new() { Id = 2, Name = "Mouse", Category = "Electronics", Price = 29.99m, Stock = 200 },
            new() { Id = 3, Name = "Desk", Category = "Furniture", Price = 399.99m, Stock = 25 },
            new() { Id = 4, Name = "Chair", Category = "Furniture", Price = 249.99m, Stock = 40 },
            new() { Id = 5, Name = "Monitor", Category = "Electronics", Price = 449.99m, Stock = 75 }
        };

        db.InsertAll(products);
        _output.WriteLine($"✓ Inserted {products.Count} products");

        // Query with LINQ
        var electronics = db.Select<Product>(p => p.Category == "Electronics");
        _output.WriteLine($"✓ Found {electronics.Count} electronics");
        Assert.Equal(3, electronics.Count);

        // Complex query
        var affordableElectronics = db.Select<Product>(p =>
            p.Category == "Electronics" && p.Price < 500);
        _output.WriteLine($"✓ Found {affordableElectronics.Count} affordable electronics");
        Assert.Equal(2, affordableElectronics.Count);

        // Query with ordering
        var topExpensive = db.Select(db.From<Product>().OrderByDescending(p => p.Price).Limit(3));
        _output.WriteLine("✓ Top 3 expensive products:");
        foreach (var product in topExpensive)
        {
            _output.WriteLine($"  - {product.Name}: {product.Price:C}");
        }
        Assert.Equal("Laptop", topExpensive[0].Name);

        // Aggregate query
        var electronicsCount = db.Count<Product>(p => p.Category == "Electronics");
        _output.WriteLine($"✓ Electronics count: {electronicsCount}");
        Assert.Equal(3, electronicsCount);
    }

    [Fact]
    public void Example_Working_With_Relationships()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Order>(overwrite: true);
        db.CreateTable<OrderItem>(overwrite: true);

        // Create order
        var order = new Order
        {
            Id = 1,
            OrderNumber = "ORD-2025-001",
            CustomerName = "John Doe",
            OrderDate = DateTime.UtcNow,
            TotalAmount = 0m
        };

        db.Insert(order);
        _output.WriteLine($"✓ Created order: {order.OrderNumber}");

        // Create order items
        var items = new List<OrderItem>
        {
            new() { Id = 1, OrderId = 1, ProductName = "Laptop", Quantity = 1, UnitPrice = 1299.99m },
            new() { Id = 2, OrderId = 1, ProductName = "Mouse", Quantity = 2, UnitPrice = 29.99m }
        };

        db.InsertAll(items);
        _output.WriteLine($"✓ Added {items.Count} items to order");

        // Calculate and update total
        var orderItems = db.Select<OrderItem>(oi => oi.OrderId == 1);
        var total = 0m;
        foreach (var item in orderItems)
        {
            total += item.Quantity * item.UnitPrice;
            _output.WriteLine($"  - {item.ProductName}: {item.Quantity} x {item.UnitPrice:C} = {item.Quantity * item.UnitPrice:C}");
        }

        order.TotalAmount = total;
        db.Update(order);
        _output.WriteLine($"✓ Order total: {total:C}");

        Assert.Equal(1359.97m, total);
    }

    [Fact]
    public void Example_Parameterized_Queries()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Product>(overwrite: true);

        db.InsertAll(new[]
        {
            new Product { Id = 1, Name = "Item A", Price = 100m, Stock = 50 },
            new Product { Id = 2, Name = "Item B", Price = 200m, Stock = 30 },
            new Product { Id = 3, Name = "Item C", Price = 150m, Stock = 40 }
        });

        // Using DuckDB parameter syntax - note: parameterized queries work differently
        var results = db.Select<Product>(p => p.Price >= 100m && p.Stock >= 35);
        results = results.OrderBy(p => p.Price).ToList();

        _output.WriteLine($"✓ Found {results.Count} products with price >= $100 and stock >= 35");
        foreach (var product in results)
        {
            _output.WriteLine($"  - {product.Name}: {product.Price:C}, Stock: {product.Stock}");
        }

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Example_Batch_Operations()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Customer>(overwrite: true);

        // Batch insert
        var customers = new List<Customer>();
        for (int i = 1; i <= 100; i++)
        {
            customers.Add(new Customer
            {
                Id = i,
                CustomerId = Guid.NewGuid(),
                Name = $"Customer {i}",
                Email = $"customer{i}@example.com",
                CreditLimit = 1000m * i,
                IsActive = i % 2 == 0,
                RegisteredAt = DateTime.UtcNow.AddDays(-i)
            });
        }

        db.InsertAll(customers);
        _output.WriteLine($"✓ Batch inserted {customers.Count} customers");

        // Batch query
        var activeCustomers = db.Select<Customer>(c => c.IsActive);
        _output.WriteLine($"✓ Found {activeCustomers.Count} active customers");
        Assert.Equal(50, activeCustomers.Count);

        // Batch update
        db.UpdateOnly(() => new Customer { IsActive = false },
            where: c => c.CreditLimit < 10000);
        _output.WriteLine("✓ Deactivated customers with credit limit < $10,000");

        var stillActive = db.Count<Customer>(c => c.IsActive);
        _output.WriteLine($"✓ Remaining active customers: {stillActive}");
        Assert.True(stillActive < 50);
    }

    [Fact]
    public void Example_Using_Transactions()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Customer>(overwrite: true);

        // Successful transaction
        using (var trans = db.OpenTransaction())
        {
            var customer1 = new Customer
            {
                Id = 1,
                CustomerId = Guid.NewGuid(),
                Name = "Customer A",
                Email = "a@example.com",
                CreditLimit = 5000m,
                IsActive = true
            };

            var customer2 = new Customer
            {
                Id = 2,
                CustomerId = Guid.NewGuid(),
                Name = "Customer B",
                Email = "b@example.com",
                CreditLimit = 10000m,
                IsActive = true
            };

            db.Insert(customer1);
            db.Insert(customer2);

            trans.Commit();
            _output.WriteLine("✓ Transaction committed successfully");
        }

        var count = db.Count<Customer>();
        Assert.Equal(2, count);
        _output.WriteLine($"✓ Total customers after commit: {count}");

        // Rollback transaction
        using (var trans = db.OpenTransaction())
        {
            var customer3 = new Customer
            {
                Id = 3,
                CustomerId = Guid.NewGuid(),
                Name = "Customer C",
                Email = "c@example.com",
                CreditLimit = 15000m,
                IsActive = true
            };

            db.Insert(customer3);
            _output.WriteLine("  - Inserted Customer C (will rollback)");

            trans.Rollback();
            _output.WriteLine("✓ Transaction rolled back");
        }

        var finalCount = db.Count<Customer>();
        Assert.Equal(2, finalCount);
        _output.WriteLine($"✓ Total customers after rollback: {finalCount}");
    }

    [Fact]
    public void Example_Working_With_DateTimes_And_Guids()
    {
        using var db = _dbFactory.Open();

        db.CreateTable<Event>(overwrite: true);

        var eventId = Guid.NewGuid();
        var startTime = new DateTime(2025, 9, 30, 10, 0, 0, DateTimeKind.Utc);
        var endTime = new DateTime(2025, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        var evt = new Event
        {
            Id = 1,
            EventId = eventId,
            Title = "Tech Conference 2025",
            StartTime = startTime,
            EndTime = endTime,
            Duration = endTime - startTime
        };

        db.Insert(evt);
        _output.WriteLine($"✓ Created event: {evt.Title}");
        _output.WriteLine($"  EventId: {evt.EventId}");
        _output.WriteLine($"  Start: {evt.StartTime:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"  End: {evt.EndTime:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"  Duration: {evt.Duration.TotalHours} hours");

        // Query by GUID
        var retrieved = db.Single<Event>(e => e.EventId == eventId);
        Assert.NotNull(retrieved);
        Assert.Equal(eventId, retrieved.EventId);
        _output.WriteLine($"✓ Retrieved event by GUID");

        // Query by date range
        var eventsToday = db.Select<Event>(e =>
            e.StartTime >= startTime.Date && e.StartTime < startTime.Date.AddDays(1));
        Assert.Single(eventsToday);
        _output.WriteLine($"✓ Found {eventsToday.Count} events on {startTime:yyyy-MM-dd}");
    }
}

// Example Models
public class Customer
{
    [PrimaryKey]
    public int Id { get; set; }

    [Index]
    public Guid CustomerId { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; }

    [Index]
    public string Email { get; set; }

    public decimal CreditLimit { get; set; }

    public bool IsActive { get; set; }

    public DateTime RegisteredAt { get; set; }
}

public class Product
{
    [PrimaryKey]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    [Index]
    public string Category { get; set; }

    public decimal Price { get; set; }

    public int Stock { get; set; }
}

public class Order
{
    [PrimaryKey]
    public int Id { get; set; }

    [Index]
    public string OrderNumber { get; set; }

    public string CustomerName { get; set; }

    public DateTime OrderDate { get; set; }

    public decimal TotalAmount { get; set; }
}

public class OrderItem
{
    [PrimaryKey]
    public int Id { get; set; }

    [Index]
    public int OrderId { get; set; }

    public string ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

public class Event
{
    [PrimaryKey]
    public int Id { get; set; }

    [Index]
    public Guid EventId { get; set; }

    public string Title { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public TimeSpan Duration { get; set; }
}
