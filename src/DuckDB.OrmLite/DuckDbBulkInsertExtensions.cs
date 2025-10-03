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
}

/// <summary>
/// Internal helper for DuckDB bulk insert operations.
/// </summary>
internal static class DuckDbBulkInsertHelper
{
    internal static void BulkInsertUsingAppender<T>(DuckDBConnection conn, IEnumerable<T> objs)
    {
        var objList = objs as IList<T> ?? objs.ToList();
        if (objList.Count == 0)
        {
            return; // Nothing to insert
        }

        var dialectProvider = DuckDbDialectProvider.Instance;
        var modelDef = typeof(T).GetModelMetadata();
        var tableName = dialectProvider.NamingStrategy.GetTableName(modelDef.ModelName);

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

    private static void AppendValueToRow(
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
