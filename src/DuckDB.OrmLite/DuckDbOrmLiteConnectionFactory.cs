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
        ConfigureDuckDbFilters();
        return new OrmLiteConnectionFactory(connectionString, DuckDbDialectProvider.Instance);
    }

    /// <summary>
    /// Extension method to open a DuckDB connection from the factory
    /// </summary>
    public static IDbConnection OpenDuckDbConnection(this OrmLiteConnectionFactory factory)
    {
        return factory.Open();
    }

    /// <summary>
    /// Configures the global OrmLite filters required for DuckDB compatibility
    /// </summary>
    private static void ConfigureDuckDbFilters()
    {
        // Only configure once
        if (OrmLiteConfig.BeforeExecFilter != null)
            return;

        OrmLiteConfig.BeforeExecFilter = dbCmd =>
        {
            var sql = dbCmd.CommandText;

            foreach (IDbDataParameter param in dbCmd.Parameters)
            {
                // Handle DbType.Currency -> Decimal conversion
                // DuckDB.NET treats DbType.Currency as VARCHAR, so we need to convert it
                if (param.DbType == DbType.Currency)
                {
                    param.DbType = DbType.Decimal;
                }

                // Handle parameter name conversion
                // OrmLite generates $p0, $p1, etc. or $0, $1, etc.
                // DuckDB.NET expects the parameter names WITHOUT the $ prefix in the Parameters collection
                // but WITH the $ prefix in the SQL
                if (param.ParameterName.StartsWith("$"))
                {
                    var nameWithoutPrefix = param.ParameterName.Substring(1);

                    // Check if it's a positional parameter (numeric)
                    if (int.TryParse(nameWithoutPrefix, out int index))
                    {
                        // DuckDB uses 1-based indexing for positional parameters
                        // OrmLite generates $0, $1, $2...
                        // We need to convert to $1, $2, $3... in the SQL
                        var newSqlParam = $"${index + 1}";
                        sql = sql.Replace(param.ParameterName, newSqlParam);

                        // The parameter name in the collection should be the 1-based index as string
                        param.ParameterName = (index + 1).ToString();
                    }
                    else
                    {
                        // Named parameter: remove $ prefix from parameter name in collection
                        param.ParameterName = nameWithoutPrefix;
                    }
                }
            }

            dbCmd.CommandText = sql;
        };
    }
}

/// <summary>
/// DuckDB-specific OrmLite connection factory
/// </summary>
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    static DuckDbOrmLiteConnectionFactory()
    {
        // Configure filters once when the class is first used
        ConfigureDuckDbFilters();
    }

    public DuckDbOrmLiteConnectionFactory(string connectionString)
        : base(connectionString, DuckDbDialectProvider.Instance)
    {
    }

    public DuckDbOrmLiteConnectionFactory(string connectionString, bool autoDisposeConnection)
        : base(connectionString, DuckDbDialectProvider.Instance, autoDisposeConnection)
    {
    }

    private static void ConfigureDuckDbFilters()
    {
        // Only configure once
        if (OrmLiteConfig.BeforeExecFilter != null)
            return;

        OrmLiteConfig.BeforeExecFilter = dbCmd =>
        {
            var sql = dbCmd.CommandText;

            foreach (IDbDataParameter param in dbCmd.Parameters)
            {
                // Handle DbType.Currency -> Decimal conversion
                // DuckDB.NET treats DbType.Currency as VARCHAR, so we need to convert it
                if (param.DbType == DbType.Currency)
                {
                    param.DbType = DbType.Decimal;
                }

                // Handle parameter name conversion
                // OrmLite generates $p0, $p1, etc. or $0, $1, etc.
                // DuckDB.NET expects the parameter names WITHOUT the $ prefix in the Parameters collection
                // but WITH the $ prefix in the SQL
                if (param.ParameterName.StartsWith("$"))
                {
                    var nameWithoutPrefix = param.ParameterName.Substring(1);

                    // Check if it's a positional parameter (numeric)
                    if (int.TryParse(nameWithoutPrefix, out int index))
                    {
                        // DuckDB uses 1-based indexing for positional parameters
                        // OrmLite generates $0, $1, $2...
                        // We need to convert to $1, $2, $3... in the SQL
                        var newSqlParam = $"${index + 1}";
                        sql = sql.Replace(param.ParameterName, newSqlParam);

                        // The parameter name in the collection should be the 1-based index as string
                        param.ParameterName = (index + 1).ToString();
                    }
                    else
                    {
                        // Named parameter: remove $ prefix from parameter name in collection
                        param.ParameterName = nameWithoutPrefix;
                    }
                }
            }

            dbCmd.CommandText = sql;
        };
    }
}
