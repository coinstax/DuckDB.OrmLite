using System.Data;
using System.IO;
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
/// DuckDB-specific OrmLite connection factory with multi-database support
/// </summary>
public class DuckDbOrmLiteConnectionFactory : OrmLiteConnectionFactory
{
    // Multi-database configuration
    private string[]? _additionalDatabases;
    private HashSet<string>? _multiDbTables;
    private bool _autoConfigureViews = true;

    // Separate dialect providers for multi-db and single-db
    private readonly DuckDbDialectProvider _multiDbDialectProvider;
    private readonly DuckDbDialectProvider _singleDbDialectProvider;

    static DuckDbOrmLiteConnectionFactory()
    {
        // Configure filters once when the class is first used
        ConfigureDuckDbFilters();
    }

    public DuckDbOrmLiteConnectionFactory(string connectionString)
        : base(connectionString, new DuckDbDialectProvider())
    {
        _multiDbDialectProvider = new DuckDbDialectProvider();
        _singleDbDialectProvider = new DuckDbDialectProvider();
    }

    public DuckDbOrmLiteConnectionFactory(string connectionString, bool autoDisposeConnection)
        : base(connectionString, new DuckDbDialectProvider(), autoDisposeConnection)
    {
        _multiDbDialectProvider = new DuckDbDialectProvider();
        _singleDbDialectProvider = new DuckDbDialectProvider();
    }

    /// <summary>
    /// Configure additional databases to attach for multi-database queries
    /// </summary>
    /// <param name="databases">Paths to additional database files</param>
    /// <returns>This factory instance for fluent configuration</returns>
    public DuckDbOrmLiteConnectionFactory WithAdditionalDatabases(params string[] databases)
    {
        _additionalDatabases = databases;
        return this;
    }

    /// <summary>
    /// Configure which tables should be queried across multiple databases
    /// </summary>
    /// <param name="tableNames">Names of tables to query across databases</param>
    /// <returns>This factory instance for fluent configuration</returns>
    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTables(params string[] tableNames)
    {
        _multiDbTables = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);

        // Configure ONLY the multi-db dialect provider
        _multiDbDialectProvider.SetMultiDatabaseTables(_multiDbTables);

        return this;
    }

    /// <summary>
    /// Configure a specific table type to be queried across multiple databases
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <returns>This factory instance for fluent configuration</returns>
    public DuckDbOrmLiteConnectionFactory WithMultiDatabaseTable<T>()
    {
        var modelDef = typeof(T).GetModelMetadata();
        _multiDbTables ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _multiDbTables.Add(modelDef.ModelName);

        // Configure ONLY the multi-db dialect provider
        _multiDbDialectProvider.SetMultiDatabaseTables(_multiDbTables);

        return this;
    }

    /// <summary>
    /// Enable or disable automatic view configuration on connection open (default: true)
    /// </summary>
    public DuckDbOrmLiteConnectionFactory WithAutoConfigureViews(bool enable)
    {
        _autoConfigureViews = enable;
        return this;
    }

    /// <summary>
    /// Opens a connection configured for multi-database reads
    /// </summary>
    public IDbConnection Open()
    {
        // If auto-configure is disabled, use single-db provider (no views)
        var shouldConfigureMultiDb = _autoConfigureViews && _additionalDatabases != null && _additionalDatabases.Length > 0;
        var providerToUse = shouldConfigureMultiDb ? _multiDbDialectProvider : _singleDbDialectProvider;

        // Temporarily swap to the appropriate dialect provider, open connection, then restore
        var originalProvider = this.DialectProvider;
        try
        {
            // Use reflection to set the DialectProvider property
            var providerProp = typeof(OrmLiteConnectionFactory).GetProperty("DialectProvider");
            providerProp?.SetValue(this, providerToUse);

            // Open connection using base factory method (which uses DialectProvider)
            var conn = this.OpenDbConnection();

            if (shouldConfigureMultiDb)
            {
                ConfigureMultiDatabase(conn);
            }

            return conn;
        }
        finally
        {
            // Restore original provider
            var providerProp = typeof(OrmLiteConnectionFactory).GetProperty("DialectProvider");
            providerProp?.SetValue(this, originalProvider);
        }
    }

    /// <summary>
    /// Opens a connection for writing to the main database only (bypasses multi-database views)
    /// </summary>
    public IDbConnection OpenForWrite()
    {
        // Temporarily swap to single-db dialect provider, open connection, then restore
        var originalProvider = this.DialectProvider;
        try
        {
            // Use reflection to set the DialectProvider property
            var providerProp = typeof(OrmLiteConnectionFactory).GetProperty("DialectProvider");
            providerProp?.SetValue(this, _singleDbDialectProvider);

            // Open connection using base factory method
            return this.OpenDbConnection();
        }
        finally
        {
            // Restore original provider
            var providerProp = typeof(OrmLiteConnectionFactory).GetProperty("DialectProvider");
            providerProp?.SetValue(this, originalProvider);
        }
    }

    /// <summary>
    /// Configures multi-database support on the connection
    /// </summary>
    private void ConfigureMultiDatabase(IDbConnection conn)
    {
        try
        {
            AttachDatabases(conn);
            CreateUnifiedViews(conn);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to configure multi-database support", ex);
        }
    }

    /// <summary>
    /// Attaches additional databases to the connection
    /// </summary>
    private void AttachDatabases(IDbConnection conn)
    {
        if (_additionalDatabases == null || _additionalDatabases.Length == 0)
            return;

        foreach (var dbPath in _additionalDatabases)
        {
            // Validate path exists
            if (!File.Exists(dbPath))
                throw new FileNotFoundException($"Database file not found: {dbPath}");

            var alias = GetDatabaseAlias(dbPath);
            var sql = $"ATTACH '{dbPath}' AS {alias}";
            try
            {
                conn.ExecuteSql(sql);
            }
            catch (Exception ex)
            {
                // Database might already be attached - ignore
            }
        }
    }

    /// <summary>
    /// Creates unified views for multi-database tables
    /// </summary>
    private void CreateUnifiedViews(IDbConnection conn)
    {
        if (_multiDbTables == null || _multiDbTables.Count == 0)
        {
            return;
        }

        foreach (var tableName in _multiDbTables)
        {
            CreateUnifiedView(conn, tableName);
        }
    }

    /// <summary>
    /// Creates a unified view for a specific table across all databases
    /// </summary>
    private void CreateUnifiedView(IDbConnection conn, string tableName)
    {
        var unionClauses = new List<string>();

        // Check if table exists in main database
        if (TableExistsInDatabase(conn, "main", tableName))
        {
            unionClauses.Add($"SELECT * FROM main.\"{tableName}\"");
        }

        // Add tables from additional databases
        if (_additionalDatabases != null)
        {
            foreach (var dbPath in _additionalDatabases)
            {
                var alias = GetDatabaseAlias(dbPath);

                // Check if table exists in this database
                if (TableExistsInDatabase(conn, alias, tableName))
                {
                    unionClauses.Add($"SELECT * FROM {alias}.\"{tableName}\"");
                }
            }
        }

        // Only create view if table exists in at least one database
        if (unionClauses.Count == 0)
        {
            return;
        }

        // Create or replace the unified view
        var viewName = $"{tableName}_Unified";
        var sql = $"CREATE OR REPLACE VIEW \"{viewName}\" AS\n{string.Join("\nUNION ALL\n", unionClauses)}";

        try
        {
            conn.ExecuteSql(sql);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create unified view '{viewName}': {ex.Message}\nSQL: {sql}", ex);
        }
    }

    /// <summary>
    /// Checks if a table exists in a specific database
    /// </summary>
    private bool TableExistsInDatabase(IDbConnection conn, string dbAlias, string tableName)
    {
        try
        {
            // Try a simple query first - if the table exists, this will succeed
            var testSql = $"SELECT COUNT(*) FROM {dbAlias}.\"{tableName}\" LIMIT 1";
            var result = conn.SqlScalar<long>(testSql);
            return true;
        }
        catch (Exception ex)
        {
            // If we can't query the table, it doesn't exist
            return false;
        }
    }

    /// <summary>
    /// Generates a valid SQL alias from a database file path
    /// </summary>
    private string GetDatabaseAlias(string dbPath)
    {
        // Extract filename without extension
        var filename = Path.GetFileNameWithoutExtension(dbPath);

        // Sanitize to valid SQL identifier (replace special characters with underscore)
        return filename
            .Replace("-", "_")
            .Replace(".", "_")
            .Replace(" ", "_");
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
