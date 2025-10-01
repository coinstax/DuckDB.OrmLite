using System.Data;
using ServiceStack.OrmLite;

namespace DuckDB.OrmLite;

public static class DuckDbOrmLiteConnectionFactoryExtensions
{
    /// <summary>
    /// Creates a new OrmLiteConnectionFactory configured for DuckDB
    /// </summary>
    /// <param name="connectionString">DuckDB connection string (e.g., "Data Source=:memory:" or "Data Source=/path/to/db.duckdb")</param>
    /// <returns>Configured OrmLiteConnectionFactory instance</returns>
    public static OrmLiteConnectionFactory CreateDuckDbConnectionFactory(string connectionString)
    {
        return new OrmLiteConnectionFactory(connectionString, DuckDbDialectProvider.Instance);
    }

    /// <summary>
    /// Extension method to open a DuckDB connection from the factory
    /// </summary>
    public static IDbConnection OpenDuckDbConnection(this OrmLiteConnectionFactory factory)
    {
        return factory.Open();
    }
}

/// <summary>
/// DuckDB-specific OrmLite connection factory
/// </summary>
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    public DuckDbOrmLiteConnectionFactory(string connectionString)
        : base(connectionString, DuckDbDialectProvider.Instance)
    {
    }

    public DuckDbOrmLiteConnectionFactory(string connectionString, bool autoDisposeConnection)
        : base(connectionString, DuckDbDialectProvider.Instance, autoDisposeConnection)
    {
    }
}
