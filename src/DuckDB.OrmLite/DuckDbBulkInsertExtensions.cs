using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DuckDB.NET.Data;
using ServiceStack.OrmLite;

namespace DuckDB.OrmLite;

/// <summary>
/// Extension methods for high-performance bulk insert operations using DuckDB's native Appender API.
/// </summary>
public static class DuckDbBulkInsertExtensions
{
    /// <summary>
    /// Performs a high-performance bulk insert using DuckDB's native Appender API.
    /// This is 10-100x faster than InsertAll for large datasets.
    ///
    /// IMPORTANT: Appender does NOT participate in transactions - it auto-commits on disposal.
    /// If you need transaction control, use InsertAll() instead.
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <param name="db">The database connection</param>
    /// <param name="objs">The objects to insert</param>
    /// <exception cref="InvalidOperationException">Thrown if the connection is not a DuckDBConnection or if insertion fails</exception>
    public static void BulkInsert<T>(this IDbConnection db, IEnumerable<T> objs)
    {
        // Unwrap the OrmLite connection to get the underlying DuckDBConnection
        var duckConn = db.ToDbConnection() as DuckDBConnection;
        if (duckConn == null)
        {
            throw new InvalidOperationException(
                "BulkInsert requires a DuckDBConnection. Use InsertAll() for other database types.");
        }

        DuckDbBulkInsertHelper.BulkInsertUsingAppender(duckConn, objs);
    }

    /// <summary>
    /// Performs a high-performance async bulk insert using DuckDB's native Appender API.
    ///
    /// NOTE: This is pseudo-async (wraps sync operation) since DuckDB.NET doesn't provide native async Appender support.
    /// IMPORTANT: Appender does NOT participate in transactions - it auto-commits on disposal.
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <param name="db">The database connection</param>
    /// <param name="objs">The objects to insert</param>
    public static System.Threading.Tasks.Task BulkInsertAsync<T>(this IDbConnection db, IEnumerable<T> objs)
    {
        return System.Threading.Tasks.Task.Run(() => BulkInsert(db, objs));
    }

    /// <summary>
    /// Performs a high-performance bulk insert with automatic deduplication using a staging table pattern.
    /// This is the RECOMMENDED approach for large tables where indexes cannot fit in memory.
    ///
    /// SAFETY: Uses a temporary staging table to validate data before inserting into the main table.
    /// This prevents corruption of large tables and provides transactional safety.
    ///
    /// PERFORMANCE: Uses DuckDB's Appender API for staging table loading (10-100x faster than InsertAll),
    /// then performs atomic INSERT SELECT with LEFT JOIN to filter duplicates.
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <param name="db">The database connection</param>
    /// <param name="objs">The objects to insert</param>
    /// <param name="uniqueKeyColumns">Column names that form the unique key for duplicate detection</param>
    /// <returns>Number of rows actually inserted (excluding duplicates)</returns>
    /// <exception cref="ArgumentException">Thrown if no unique key columns are specified</exception>
    /// <exception cref="InvalidOperationException">Thrown if the connection is not a DuckDBConnection or if insertion fails</exception>
    public static int BulkInsertWithDeduplication<T>(
        this IDbConnection db,
        IEnumerable<T> objs,
        params string[] uniqueKeyColumns)
    {
        if (uniqueKeyColumns == null || uniqueKeyColumns.Length == 0)
        {
            throw new ArgumentException(
                "At least one unique key column must be specified for deduplication. " +
                "Use BulkInsert() if you don't need duplicate checking.",
                nameof(uniqueKeyColumns));
        }

        var objList = objs as IList<T> ?? objs.ToList();
        if (objList.Count == 0)
        {
            return 0; // Nothing to insert
        }

        var dialectProvider = DuckDbDialectProvider.Instance;
        var modelDef = typeof(T).GetModelMetadata();
        var mainTableName = dialectProvider.NamingStrategy.GetTableName(modelDef.ModelName);
        var stagingTableName = $"{mainTableName}_Staging_{Guid.NewGuid():N}";

        try
        {
            // Step 1: Create staging table with same schema as main table
            var createStagingSql = GenerateCreateStagingTableSql<T>(stagingTableName, dialectProvider, modelDef);
            db.ExecuteSql(createStagingSql);

            // Step 2: BulkInsert into staging table (fast, isolated)
            BulkInsertToTable(db, objList, stagingTableName);

            // Step 3: Insert from staging to main, checking for duplicates
            var insertSql = GenerateDeduplicatedInsertSql(
                mainTableName,
                stagingTableName,
                uniqueKeyColumns,
                modelDef,
                dialectProvider);

            var insertedCount = db.ExecuteSql(insertSql);

            return insertedCount;
        }
        finally
        {
            // Step 4: Always cleanup staging table
            try
            {
                db.ExecuteSql($"DROP TABLE IF EXISTS \"{stagingTableName}\"");
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Performs a high-performance bulk insert with automatic deduplication, auto-detecting unique columns.
    /// Uses [Unique], [Index(Unique=true)], or [CompositeIndex] attributes to determine unique key.
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <param name="db">The database connection</param>
    /// <param name="objs">The objects to insert</param>
    /// <returns>Number of rows actually inserted (excluding duplicates)</returns>
    /// <exception cref="InvalidOperationException">Thrown if no unique columns can be detected from model attributes</exception>
    public static int BulkInsertWithDeduplication<T>(this IDbConnection db, IEnumerable<T> objs)
    {
        var uniqueColumns = ExtractUniqueColumnsFromModel<T>();

        if (uniqueColumns.Length == 0)
        {
            throw new InvalidOperationException(
                $"No unique columns found on type {typeof(T).Name}. " +
                "Use [Unique], [Index(Unique=true)], or [CompositeIndex] attributes, " +
                "or use the overload with explicit uniqueKeyColumns parameter, " +
                "or use the LINQ expression overload: db.BulkInsertWithDeduplication(records, x => new {{ x.Col1, x.Col2 }})");
        }

        return BulkInsertWithDeduplication(db, objs, uniqueColumns);
    }

    /// <summary>
    /// Performs a high-performance bulk insert with automatic deduplication using a LINQ expression to specify unique columns.
    /// Type-safe alternative to string-based column names.
    /// </summary>
    /// <typeparam name="T">The model type</typeparam>
    /// <param name="db">The database connection</param>
    /// <param name="objs">The objects to insert</param>
    /// <param name="uniqueKeySelector">LINQ expression selecting the unique key columns.
    /// Single column: x => x.Email
    /// Multiple columns: x => new { x.Timestamp, x.Symbol }</param>
    /// <returns>Number of rows actually inserted (excluding duplicates)</returns>
    /// <exception cref="ArgumentException">Thrown if expression doesn't select valid columns</exception>
    /// <example>
    /// // Single column
    /// db.BulkInsertWithDeduplication(users, x => x.Email);
    ///
    /// // Multiple columns
    /// db.BulkInsertWithDeduplication(prices, x => new { x.Timestamp, x.Symbol });
    /// </example>
    public static int BulkInsertWithDeduplication<T>(
        this IDbConnection db,
        IEnumerable<T> objs,
        System.Linq.Expressions.Expression<Func<T, object>> uniqueKeySelector)
    {
        var columnNames = ExtractColumnNamesFromExpression(uniqueKeySelector);
        return BulkInsertWithDeduplication(db, objs, columnNames);
    }

    /// <summary>
    /// Async version of BulkInsertWithDeduplication with explicit unique key columns.
    /// NOTE: This is pseudo-async (wraps sync operation).
    /// </summary>
    public static System.Threading.Tasks.Task<int> BulkInsertWithDeduplicationAsync<T>(
        this IDbConnection db,
        IEnumerable<T> objs,
        params string[] uniqueKeyColumns)
    {
        return System.Threading.Tasks.Task.Run(() => BulkInsertWithDeduplication(db, objs, uniqueKeyColumns));
    }

    /// <summary>
    /// Async version of BulkInsertWithDeduplication with auto-detected unique columns.
    /// NOTE: This is pseudo-async (wraps sync operation).
    /// </summary>
    public static System.Threading.Tasks.Task<int> BulkInsertWithDeduplicationAsync<T>(
        this IDbConnection db,
        IEnumerable<T> objs)
    {
        return System.Threading.Tasks.Task.Run(() => BulkInsertWithDeduplication(db, objs));
    }

    /// <summary>
    /// Async version of BulkInsertWithDeduplication with LINQ expression for unique key.
    /// NOTE: This is pseudo-async (wraps sync operation).
    /// </summary>
    public static System.Threading.Tasks.Task<int> BulkInsertWithDeduplicationAsync<T>(
        this IDbConnection db,
        IEnumerable<T> objs,
        System.Linq.Expressions.Expression<Func<T, object>> uniqueKeySelector)
    {
        return System.Threading.Tasks.Task.Run(() => BulkInsertWithDeduplication(db, objs, uniqueKeySelector));
    }

    /// <summary>
    /// Internal method to bulk insert into a specific table name
    /// </summary>
    private static void BulkInsertToTable<T>(IDbConnection db, IEnumerable<T> objs, string tableName)
    {
        var duckConn = db.ToDbConnection() as DuckDBConnection;
        if (duckConn == null)
        {
            throw new InvalidOperationException(
                "BulkInsertWithDeduplication requires a DuckDBConnection.");
        }

        // Use the same logic as the main BulkInsert method
        DuckDbBulkInsertHelper.BulkInsertUsingAppender(duckConn, objs, tableName);
    }

    /// <summary>
    /// Generates CREATE TABLE statement for staging table by copying the main table's schema.
    /// Uses CREATE TABLE AS SELECT ... LIMIT 0 to get an exact schema copy without data.
    /// This preserves all column types (including DECIMAL precision/scale) exactly as defined in the database.
    /// </summary>
    private static string GenerateCreateStagingTableSql<T>(
        string stagingTableName,
        DuckDbDialectProvider dialectProvider,
        ServiceStack.OrmLite.ModelDefinition modelDef)
    {
        var mainTableName = dialectProvider.NamingStrategy.GetTableName(modelDef.ModelName);

        // Get non-auto-increment columns to copy
        var fields = modelDef.FieldDefinitions
            .Where(f => !f.AutoIncrement)
            .Select(f => $"\"{f.FieldName}\"")
            .ToList();

        var columnList = string.Join(", ", fields);

        // Create staging table with exact schema from main table (without data or constraints)
        // SELECT ... LIMIT 0 copies the schema perfectly including DECIMAL(38,6) etc.
        // DuckDB doesn't copy constraints with CREATE TABLE AS SELECT
        var sql = $"CREATE TABLE \"{stagingTableName}\" AS SELECT {columnList} FROM \"{mainTableName}\" LIMIT 0";

        return sql;
    }

    /// <summary>
    /// Generates INSERT SELECT SQL with LEFT JOIN for deduplication.
    /// Uses CTE with ROW_NUMBER to handle internal duplicates in the staging table.
    /// </summary>
    private static string GenerateDeduplicatedInsertSql(
        string mainTableName,
        string stagingTableName,
        string[] uniqueKeyColumns,
        ServiceStack.OrmLite.ModelDefinition modelDef,
        DuckDbDialectProvider dialectProvider)
    {
        var quotedMainTable = $"\"{mainTableName}\"";
        var quotedStagingTable = $"\"{stagingTableName}\"";

        // Get non-auto-increment fields (same as staging table)
        var fields = modelDef.FieldDefinitions
            .Where(f => !f.AutoIncrement)
            .ToList();

        // Build column list for INSERT
        var columnNames = string.Join(", ", fields.Select(f => $"\"{f.FieldName}\""));
        var selectColumns = string.Join(", ", fields.Select(f => $"s.\"{f.FieldName}\""));

        // Build PARTITION BY clause for ROW_NUMBER (to deduplicate staging table internally)
        var partitionByClause = string.Join(", ", uniqueKeyColumns.Select(col => $"\"{col}\""));

        // Build JOIN conditions
        var joinConditions = string.Join(" AND ",
            uniqueKeyColumns.Select(col => $"s.\"{col}\" = m.\"{col}\""));

        // Build WHERE clause (check if main table key is NULL = not exists, and rn = 1 for internal deduplication)
        var whereClause = $"m.\"{uniqueKeyColumns[0]}\" IS NULL AND s.rn = 1";

        // Use CTE to deduplicate staging table internally before checking against main table
        var sql = $@"
INSERT INTO {quotedMainTable} ({columnNames})
WITH DeduplicatedStaging AS (
    SELECT *,
           ROW_NUMBER() OVER (PARTITION BY {partitionByClause} ORDER BY (SELECT NULL)) as rn
    FROM {quotedStagingTable}
)
SELECT {selectColumns}
FROM DeduplicatedStaging s
LEFT JOIN {quotedMainTable} m ON {joinConditions}
WHERE {whereClause}";

        return sql;
    }

    /// <summary>
    /// Extracts unique columns from model attributes.
    /// Priority: CompositeKey > CompositeIndex(Unique=true) > Individual [Unique]/[Index(Unique=true)]
    /// Throws if multiple composite constraints exist (ambiguous).
    /// </summary>
    private static string[] ExtractUniqueColumnsFromModel<T>()
    {
        var modelDef = typeof(T).GetModelMetadata();

        // Priority 1: Check for CompositeKey on the class
        var compositeKeys = typeof(T).GetCustomAttributes(typeof(ServiceStack.DataAnnotations.CompositeKeyAttribute), true)
            .Cast<ServiceStack.DataAnnotations.CompositeKeyAttribute>()
            .ToList();

        if (compositeKeys.Count > 1)
        {
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} has multiple [CompositeKey] attributes. " +
                "Only one composite constraint is allowed for auto-detection. " +
                "Use the explicit uniqueKeyColumns parameter to specify which columns to use.");
        }

        if (compositeKeys.Count == 1)
        {
            // Use CompositeKey columns - this is the primary key constraint
            return compositeKeys[0].FieldNames.ToArray();
        }

        // Priority 2: Check for CompositeIndex with Unique=true on the class
        var compositeIndexes = typeof(T).GetCustomAttributes(typeof(ServiceStack.DataAnnotations.CompositeIndexAttribute), true)
            .Cast<ServiceStack.DataAnnotations.CompositeIndexAttribute>()
            .Where(idx => idx.Unique)
            .ToList();

        if (compositeIndexes.Count > 1)
        {
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} has multiple [CompositeIndex(Unique=true)] attributes. " +
                "Only one composite unique constraint is allowed for auto-detection. " +
                "Use the explicit uniqueKeyColumns parameter to specify which columns to use.");
        }

        if (compositeIndexes.Count == 1)
        {
            // Use CompositeIndex unique columns
            return compositeIndexes[0].FieldNames.ToArray();
        }

        // Priority 3: Check for individual [Unique] or [Index(Unique=true)] attributes on fields
        var uniqueColumns = new List<string>();

        foreach (var field in modelDef.FieldDefinitions)
        {
            var uniqueAttr = field.PropertyInfo?.GetCustomAttributes(typeof(ServiceStack.DataAnnotations.UniqueAttribute), true)
                .FirstOrDefault() as ServiceStack.DataAnnotations.UniqueAttribute;

            if (uniqueAttr != null)
            {
                uniqueColumns.Add(field.FieldName);
            }
            else
            {
                var indexAttr = field.PropertyInfo?.GetCustomAttributes(typeof(ServiceStack.DataAnnotations.IndexAttribute), true)
                    .FirstOrDefault() as ServiceStack.DataAnnotations.IndexAttribute;

                if (indexAttr?.Unique == true)
                {
                    uniqueColumns.Add(field.FieldName);
                }
            }
        }

        if (uniqueColumns.Count > 1)
        {
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} has multiple [Unique] or [Index(Unique=true)] attributes: {string.Join(", ", uniqueColumns)}. " +
                "Multiple individual unique constraints cannot be combined for deduplication. " +
                "Use the explicit uniqueKeyColumns parameter to specify which column(s) to use for duplicate checking.");
        }

        if (uniqueColumns.Count == 1)
        {
            // Single unique column
            return uniqueColumns.ToArray();
        }

        // No unique constraints found
        return Array.Empty<string>();
    }

    /// <summary>
    /// Extracts column names from a LINQ expression
    /// </summary>
    private static string[] ExtractColumnNamesFromExpression<T>(System.Linq.Expressions.Expression<Func<T, object>> expression)
    {
        var columnNames = new List<string>();

        // Handle the body of the expression
        var body = expression.Body;

        // Remove any Convert/ConvertChecked wrapper (e.g., from boxing value types)
        if (body is System.Linq.Expressions.UnaryExpression unary &&
            (unary.NodeType == System.Linq.Expressions.ExpressionType.Convert ||
             unary.NodeType == System.Linq.Expressions.ExpressionType.ConvertChecked))
        {
            body = unary.Operand;
        }

        // Case 1: Single property access - x => x.Email
        if (body is System.Linq.Expressions.MemberExpression memberExpr)
        {
            columnNames.Add(memberExpr.Member.Name);
        }
        // Case 2: Anonymous type - x => new { x.Timestamp, x.Symbol }
        else if (body is System.Linq.Expressions.NewExpression newExpr)
        {
            foreach (var arg in newExpr.Arguments)
            {
                if (arg is System.Linq.Expressions.MemberExpression memberArg)
                {
                    columnNames.Add(memberArg.Member.Name);
                }
                else
                {
                    throw new ArgumentException(
                        $"Invalid expression argument: {arg}. " +
                        "Only property/field access is supported in the unique key selector.");
                }
            }
        }
        else
        {
            throw new ArgumentException(
                $"Invalid expression: {expression}. " +
                "Use either a single property (x => x.Email) or anonymous type (x => new {{ x.Col1, x.Col2 }})");
        }

        if (columnNames.Count == 0)
        {
            throw new ArgumentException(
                "No columns found in expression. Ensure you're selecting at least one property.");
        }

        return columnNames.ToArray();
    }
}

/// <summary>
/// Internal helper for DuckDB bulk insert operations.
/// </summary>
internal static class DuckDbBulkInsertHelper
{
    internal static void BulkInsertUsingAppender<T>(DuckDBConnection conn, IEnumerable<T> objs, string tableName = null)
    {
        var objList = objs as IList<T> ?? objs.ToList();
        if (objList.Count == 0)
        {
            return; // Nothing to insert
        }

        var dialectProvider = DuckDbDialectProvider.Instance;
        var modelDef = typeof(T).GetModelMetadata();
        tableName = tableName ?? dialectProvider.NamingStrategy.GetTableName(modelDef.ModelName);

        // Get fields in correct order (same as table definition)
        // Skip auto-increment primary keys as DuckDB will generate these
        // Also skip any field with AutoIncrement (even if not PK) since they have sequences
        var fields = modelDef.FieldDefinitions
            .Where(f => !f.AutoIncrement)
            .ToList();

        if (fields.Count == 0)
        {
            throw new InvalidOperationException(
                $"No insertable fields found for type {typeof(T).Name}. " +
                "Ensure the model has non-auto-increment fields.");
        }

        try
        {
            using var appender = conn.CreateAppender(tableName);

            foreach (var obj in objList)
            {
                var row = appender.CreateRow();

                foreach (var field in fields)
                {
                    var value = field.GetValue(obj);
                    AppendValueToRow(row, dialectProvider, field, value);
                }

                row.EndRow();
            }

            // Appender.Dispose() commits the batch automatically
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"DuckDB bulk insert failed for table '{tableName}'. " +
                $"Inserted 0 of {objList.Count} rows. " +
                $"Ensure all field types match table schema exactly. " +
                $"See inner exception for details.",
                ex);
        }
    }

    internal static void AppendValueToRow(
        DuckDB.NET.Data.IDuckDBAppenderRow row,
        DuckDbDialectProvider dialectProvider,
        ServiceStack.OrmLite.FieldDefinition fieldDef,
        object value)
    {
        if (value == null)
        {
            row.AppendNullValue();
            return;
        }

        // For Appender, we DON'T use the type converter because:
        // 1. Appender expects native types (TimeSpan, not string)
        // 2. Converters are for SQL statement generation, not binary appending
        // 3. DuckDB Appender API handles type conversion internally

        // Handle the value based on its actual type (after conversion)
        // The Appender API requires strongly-typed method calls
        switch (value)
        {
            case DBNull _:
                row.AppendNullValue();
                break;
            case bool boolValue:
                row.AppendValue(boolValue);
                break;
            case byte byteValue:
                row.AppendValue(byteValue);
                break;
            case sbyte sbyteValue:
                row.AppendValue(sbyteValue);
                break;
            case short shortValue:
                row.AppendValue(shortValue);
                break;
            case ushort ushortValue:
                row.AppendValue(ushortValue);
                break;
            case int intValue:
                row.AppendValue(intValue);
                break;
            case uint uintValue:
                row.AppendValue(uintValue);
                break;
            case long longValue:
                row.AppendValue(longValue);
                break;
            case ulong ulongValue:
                row.AppendValue(ulongValue);
                break;
            case float floatValue:
                row.AppendValue(floatValue);
                break;
            case double doubleValue:
                row.AppendValue(doubleValue);
                break;
            case decimal decimalValue:
                row.AppendValue(decimalValue);
                break;
            case string stringValue:
                row.AppendValue(stringValue);
                break;
            case DateTime dateTimeValue:
                row.AppendValue(dateTimeValue);
                break;
            case DateTimeOffset dateTimeOffsetValue:
                row.AppendValue(dateTimeOffsetValue.DateTime);
                break;
            case TimeSpan timeSpanValue:
                row.AppendValue(timeSpanValue);
                break;
            case Guid guidValue:
                row.AppendValue(guidValue);
                break;
            case byte[] byteArrayValue:
                row.AppendValue(byteArrayValue);
                break;
            default:
                // For unknown types, try to append as object
                // This may fail at runtime if DuckDB doesn't support the type
                throw new NotSupportedException(
                    $"Type {value.GetType().Name} is not supported for bulk insert. " +
                    $"Field: {fieldDef.FieldName}, Value: {value}");
        }
    }
}
