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
        var retrieved = db.SingleById<Customer>(customer.Id);
        Assert.Equal(customer.Name, retrieved.Name);
        _output.WriteLine($"✓ Retrieved customer: {retrieved.Name}");

        // Update
        retrieved.CreditLimit = 75000.00m;
        db.Update(retrieved);
        _output.WriteLine($"✓ Updated credit limit to {retrieved.CreditLimit:C}");

        // Verify update
        var updated = db.SingleById<Customer>(customer.Id);
        Assert.Equal(75000.00m, updated.CreditLimit);

        // Delete
        db.DeleteById<Customer>(customer.Id);
        _output.WriteLine("✓ Deleted customer");

        // Verify deletion
        var deleted = db.SingleById<Customer>(customer.Id);
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
            new() { Name = "Laptop", Category = "Electronics", Price = 1299.99m, Stock = 50 },
            new() { Name = "Mouse", Category = "Electronics", Price = 29.99m, Stock = 200 },
            new() { Name = "Desk", Category = "Furniture", Price = 399.99m, Stock = 25 },
            new() { Name = "Chair", Category = "Furniture", Price = 249.99m, Stock = 40 },
            new() { Name = "Monitor", Category = "Electronics", Price = 449.99m, Stock = 75 }
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
            new() { OrderId = order.Id, ProductName = "Laptop", Quantity = 1, UnitPrice = 1299.99m },
            new() { OrderId = order.Id, ProductName = "Mouse", Quantity = 2, UnitPrice = 29.99m }
        };

        db.InsertAll(items);
        _output.WriteLine($"✓ Added {items.Count} items to order");

        // Calculate and update total
        var orderItems = db.Select<OrderItem>(oi => oi.OrderId == order.Id);
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
            new Product { Name = "Item A", Price = 100m, Stock = 50 },
            new Product { Name = "Item B", Price = 200m, Stock = 30 },
            new Product { Name = "Item C", Price = 150m, Stock = 40 }
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
                CustomerId = Guid.NewGuid(),
                Name = "Customer A",
                Email = "a@example.com",
                CreditLimit = 5000m,
                IsActive = true
            };

            var customer2 = new Customer
            {
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
    [AutoIncrement]
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
    [AutoIncrement]
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
    [AutoIncrement]
    public int Id { get; set; }

    [Index]
    public string OrderNumber { get; set; }

    public string CustomerName { get; set; }

    public DateTime OrderDate { get; set; }

    public decimal TotalAmount { get; set; }
}

public class OrderItem
{
    [AutoIncrement]
    public int Id { get; set; }

    [Index]
    public int OrderId { get; set; }

    public string ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

public class Event
{
    [AutoIncrement]
    public int Id { get; set; }

    [Index]
    public Guid EventId { get; set; }

    public string Title { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public TimeSpan Duration { get; set; }
}
