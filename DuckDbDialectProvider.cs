using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Converters;

namespace ServiceStack.OrmLite.DuckDb;

public class DuckDbDialectProvider : OrmLiteDialectProviderBase<DuckDbDialectProvider>
{
    public static DuckDbDialectProvider Instance = new();

    public DuckDbDialectProvider()
    {
        base.AutoIncrementDefinition = "";  // DuckDB doesn't use a special AUTO_INCREMENT keyword
        base.DefaultValueFormat = " DEFAULT {0}";
        base.ParamString = "$";  // DuckDB uses $name for named parameters

        // Set naming strategy
        base.NamingStrategy = new OrmLiteNamingStrategyBase();


        // Initialize type converters
        base.RegisterConverter<string>(new DuckDbStringConverter());
        base.RegisterConverter<bool>(new DuckDbBoolConverter());
        base.RegisterConverter<Guid>(new DuckDbGuidConverter());
        base.RegisterConverter<DateTime>(new DuckDbDateTimeConverter());
        base.RegisterConverter<DateTimeOffset>(new DuckDbDateTimeOffsetConverter());
        base.RegisterConverter<TimeSpan>(new DuckDbTimeSpanConverter());
        base.RegisterConverter<decimal>(new DuckDbDecimalConverter());
        base.RegisterConverter<byte[]>(new DuckDbByteArrayConverter());

        // Integer types
        base.RegisterConverter<sbyte>(new DuckDbSByteConverter());
        base.RegisterConverter<byte>(new DuckDbByteConverter());
        base.RegisterConverter<short>(new DuckDbInt16Converter());
        base.RegisterConverter<ushort>(new DuckDbUInt16Converter());
        base.RegisterConverter<int>(new DuckDbInt32Converter());
        base.RegisterConverter<uint>(new DuckDbUInt32Converter());
        base.RegisterConverter<long>(new DuckDbInt64Converter());
        base.RegisterConverter<ulong>(new DuckDbUInt64Converter());

        // Floating point types
        base.RegisterConverter<float>(new DuckDbFloatConverter());
        base.RegisterConverter<double>(new DuckDbDoubleConverter());

        this.Variables = new Dictionary<string, string>
        {
            { OrmLiteVariables.SystemUtc, "CURRENT_TIMESTAMP" },
            { OrmLiteVariables.MaxText, "VARCHAR" },
            { OrmLiteVariables.MaxTextUnicode, "VARCHAR" },
            { OrmLiteVariables.True, "TRUE" },
            { OrmLiteVariables.False, "FALSE" },
        };
    }

    public override string GetQuotedValue(string paramValue)
    {
        return "'" + paramValue.Replace("'", "''") + "'";
    }

    public override IDbConnection CreateConnection(string connectionString, Dictionary<string, string>? options)
    {
        return new DuckDB.NET.Data.DuckDBConnection(connectionString);
    }

    public override bool ShouldQuote(string name)
    {
        // Always quote identifiers in DuckDB to avoid reserved word conflicts
        return true;
    }

    public override string GetQuotedColumnName(string columnName)
    {
        return $"\"{columnName}\"";
    }

    public override string GetQuotedName(string name)
    {
        return $"\"{name}\"";
    }

    public override string GetQuotedTableName(ModelDefinition modelDef)
    {
        return GetQuotedName(NamingStrategy.GetTableName(modelDef.ModelName));
    }

    public override string GetColumnDefinition(FieldDefinition fieldDef)
    {
        // For auto-increment primary keys, just mark as INTEGER PRIMARY KEY
        // We'll handle the DEFAULT nextval() in ToCreateTableStatement
        if (fieldDef.AutoIncrement && fieldDef.IsPrimaryKey)
        {
            return $"{GetQuotedColumnName(fieldDef.FieldName)} INTEGER PRIMARY KEY";
        }

        // For other auto-increment fields
        if (fieldDef.AutoIncrement)
        {
            return $"{GetQuotedColumnName(fieldDef.FieldName)} INTEGER";
        }

        return base.GetColumnDefinition(fieldDef);
    }

    public override string ToCreateTableStatement(Type tableType)
    {
        var modelDef = GetModel(tableType);
        var sbSql = new StringBuilder();

        // Create sequences for auto-increment fields first
        foreach (var fieldDef in modelDef.FieldDefinitions)
        {
            if (fieldDef.AutoIncrement)
            {
                var sequenceName = $"seq_{modelDef.ModelName}_{fieldDef.FieldName}".ToLower();
                sbSql.AppendLine($"CREATE SEQUENCE IF NOT EXISTS \"{sequenceName}\" START 1;");
            }
        }

        // Get the base CREATE TABLE statement
        var baseStatement = base.ToCreateTableStatement(tableType);

        // Now we need to inject DEFAULT nextval() for auto-increment columns
        // Parse and modify the base statement
        foreach (var fieldDef in modelDef.FieldDefinitions)
        {
            if (fieldDef.AutoIncrement)
            {
                var sequenceName = $"seq_{modelDef.ModelName}_{fieldDef.FieldName}".ToLower();
                var columnName = GetQuotedColumnName(fieldDef.FieldName);

                // Find the column definition and add DEFAULT nextval()
                // Look for pattern like "ColumnName" INTEGER PRIMARY KEY
                var searchPattern = $"{columnName} INTEGER";
                var replacement = $"{columnName} INTEGER DEFAULT nextval('\"{sequenceName}\"')";

                baseStatement = baseStatement.Replace(searchPattern, replacement);
            }
        }

        sbSql.Append(baseStatement);
        return sbSql.ToString();
    }

    public override bool DoesTableExist(IDbCommand dbCmd, string tableName, string? schema = null)
    {
        var sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = $tableName";

        dbCmd.CommandText = sql;
        dbCmd.Parameters.Clear();
        var p = CreateParam();
        p.ParameterName = "tableName";  // Named parameter without $ prefix
        p.Value = tableName;
        dbCmd.Parameters.Add(p);

        var result = dbCmd.ExecuteScalar();
        return result != null && Convert.ToInt64(result) > 0;
    }

    public override string ToExistStatement(Type fromTableType, object objWithProperties, string sqlFilter, params object[] filterParams)
    {
        var modelDef = GetModel(fromTableType);
        var sql = $"SELECT EXISTS(SELECT 1 FROM {GetQuotedTableName(modelDef)} WHERE {sqlFilter})";
        return sql;
    }

    protected override string GetIndexName(bool isUnique, string modelName, string fieldName)
    {
        return $"{(isUnique ? "uidx" : "idx")}_{modelName}_{fieldName}".ToLower();
    }

    public override string GetLastInsertIdSqlSuffix<T>()
    {
        // DuckDB supports RETURNING clause for getting the inserted ID
        var modelDef = GetModel(typeof(T));
        var pkField = modelDef.PrimaryKey;
        if (pkField != null)
        {
            return $" RETURNING {GetQuotedColumnName(pkField.FieldName)}";
        }
        return string.Empty;
    }

    public override bool HasInsertReturnValues(ModelDefinition modelDef)
    {
        // DuckDB supports RETURNING clause
        return modelDef.FieldDefinitions.Any(x => x.ReturnOnInsert || (x.AutoIncrement && x.IsPrimaryKey));
    }

    public override IDbDataParameter CreateParam()
    {
        return new DuckDB.NET.Data.DuckDBParameter();
    }

    public override bool DoesSchemaExist(IDbCommand dbCmd, string schemaName)
    {
        var sql = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = $schemaName";
        dbCmd.CommandText = sql;
        dbCmd.Parameters.Clear();
        var p = CreateParam();
        p.ParameterName = "schemaName";
        p.Value = schemaName;
        dbCmd.Parameters.Add(p);

        var result = dbCmd.ExecuteScalar();
        return result != null && Convert.ToInt64(result) > 0;
    }

    public override string ToCreateSchemaStatement(string schemaName)
    {
        return $"CREATE SCHEMA IF NOT EXISTS {GetQuotedName(schemaName)}";
    }

    public override SqlExpression<T> SqlExpression<T>()
    {
        return new DuckDbSqlExpression<T>(this);
    }

    public override void InitQueryParam(IDbDataParameter p)
    {
        base.InitQueryParam(p);
        // Don't strip $ here - let SqlExpression use it to generate SQL with $ placeholders
        // We'll strip it in BeforeExecFilter right before DuckDB.NET execution
    }

    public override void SetParameterValues<T>(IDbCommand dbCmd, object obj)
    {
        // Call base to populate values
        base.SetParameterValues<T>(dbCmd, obj);
        // Parameter name conversion handled in BeforeExecFilter
    }
}
