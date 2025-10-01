using System;
using ServiceStack.OrmLite;

namespace ServiceStack.OrmLite.DuckDb;

/// <summary>
/// DuckDB-specific SQL expression builder for LINQ queries
/// DuckDB uses PostgreSQL-compatible SQL syntax, so we can rely on the base implementation
/// </summary>
public class DuckDbSqlExpression<T> : SqlExpression<T>
{
    public DuckDbSqlExpression(IOrmLiteDialectProvider dialectProvider)
        : base(dialectProvider)
    {
    }

    // DuckDB uses standard SQL syntax compatible with the base SqlExpression
    // No custom overrides needed for basic operations
}
