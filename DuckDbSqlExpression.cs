using System;
using System.Collections.Generic;
using System.Data;
using ServiceStack.OrmLite;

namespace ServiceStack.OrmLite.DuckDb;

/// <summary>
/// DuckDB-specific SQL expression builder for LINQ queries
/// Uses named parameters like $p0, $p1 instead of positional $0, $1 to avoid DuckDB.NET type inference issues
/// </summary>
public class DuckDbSqlExpression<T> : SqlExpression<T>
{
    private int _paramCounter = 0;

    public DuckDbSqlExpression(IOrmLiteDialectProvider dialectProvider)
        : base(dialectProvider)
    {
    }

    public override IDbDataParameter AddParam(object value)
    {
        // Generate named parameters like p0, p1, p2 instead of positional 0, 1, 2
        var paramName = $"p{_paramCounter++}";
        var paramValue = value;
        var parameter = CreateParam(paramName, paramValue);

        // DuckDB.NET infers types from the .NET runtime type of the Value property
        // Don't convert the value - keep it as-is so DuckDB.NET can infer correctly
        parameter.Value = paramValue ?? DBNull.Value;

        DialectProvider.InitQueryParam(parameter);
        Params.Add(parameter);
        return parameter;
    }
}
