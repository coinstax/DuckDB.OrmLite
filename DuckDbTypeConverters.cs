using System;
using System.Data;
using System.Numerics;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Converters;

namespace ServiceStack.OrmLite.DuckDb;

public class DuckDbGuidConverter : GuidConverter
{
    public override string ColumnDefinition => "UUID";

    public override DbType DbType => DbType.Guid;
}

public class DuckDbBigIntegerConverter : OrmLiteConverter
{
    public override string ColumnDefinition => "HUGEINT";

    public override DbType DbType => DbType.Object;

    public override object FromDbValue(Type fieldType, object value)
    {
        if (value == null || value is DBNull)
            return BigInteger.Zero;

        if (value is BigInteger bi)
            return bi;

        if (value is long l)
            return new BigInteger(l);

        if (value is string s)
            return BigInteger.Parse(s);

        return BigInteger.Parse(value.ToString()!);
    }

    public override object ToDbValue(Type fieldType, object value)
    {
        if (value == null)
            return DBNull.Value;

        return value;
    }
}

public class DuckDbDateTimeConverter : DateTimeConverter
{
    public override string ColumnDefinition => "TIMESTAMP";

    public override DbType DbType => DbType.DateTime;

    public override string ToQuotedString(Type fieldType, object value)
    {
        var dateTime = (DateTime)value;
        return $"'{dateTime:yyyy-MM-dd HH:mm:ss.ffffff}'::TIMESTAMP";
    }
}

public class DuckDbDateTimeOffsetConverter : DateTimeOffsetConverter
{
    public override string ColumnDefinition => "TIMESTAMPTZ";

    public override DbType DbType => DbType.DateTimeOffset;

    public override string ToQuotedString(Type fieldType, object value)
    {
        var dateTimeOffset = (DateTimeOffset)value;
        return $"'{dateTimeOffset:yyyy-MM-dd HH:mm:ss.ffffffzzz}'::TIMESTAMPTZ";
    }
}

public class DuckDbTimeSpanConverter : TimeSpanAsIntConverter
{
    public override string ColumnDefinition => "INTERVAL";

    public override DbType DbType => DbType.Object;

    public override string ToQuotedString(Type fieldType, object value)
    {
        var timeSpan = (TimeSpan)value;
        return $"INTERVAL '{timeSpan.TotalSeconds} seconds'";
    }
}

public class DuckDbBoolConverter : BoolConverter
{
    public override string ColumnDefinition => "BOOLEAN";

    public override DbType DbType => DbType.Boolean;
}

public class DuckDbStringConverter : StringConverter
{
    public override string ColumnDefinition => "VARCHAR";

    public override string MaxColumnDefinition => "VARCHAR";

    public override DbType DbType => DbType.String;

    public override string GetColumnDefinition(int? stringLength)
    {
        // DuckDB VARCHAR doesn't require length specification
        return "VARCHAR";
    }
}

public class DuckDbDecimalConverter : DecimalConverter
{
    public override string ColumnDefinition => "DECIMAL(18,6)";

    public override DbType DbType => DbType.Decimal;
}

public class DuckDbByteArrayConverter : ByteArrayConverter
{
    public override string ColumnDefinition => "BLOB";

    public override DbType DbType => DbType.Binary;
}

// Integer type converters
public class DuckDbSByteConverter : SByteConverter
{
    public override string ColumnDefinition => "TINYINT";
}

public class DuckDbByteConverter : ByteConverter
{
    public override string ColumnDefinition => "UTINYINT";
}

public class DuckDbInt16Converter : Int16Converter
{
    public override string ColumnDefinition => "SMALLINT";
}

public class DuckDbUInt16Converter : UInt16Converter
{
    public override string ColumnDefinition => "USMALLINT";
}

public class DuckDbInt32Converter : Int32Converter
{
    public override string ColumnDefinition => "INTEGER";
}

public class DuckDbUInt32Converter : UInt32Converter
{
    public override string ColumnDefinition => "UINTEGER";
}

public class DuckDbInt64Converter : Int64Converter
{
    public override string ColumnDefinition => "BIGINT";
}

public class DuckDbUInt64Converter : UInt64Converter
{
    public override string ColumnDefinition => "UBIGINT";
}

public class DuckDbFloatConverter : FloatConverter
{
    public override string ColumnDefinition => "REAL";
}

public class DuckDbDoubleConverter : DoubleConverter
{
    public override string ColumnDefinition => "DOUBLE";
}
