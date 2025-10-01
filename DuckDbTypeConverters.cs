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

    public override object ToDbValue(Type fieldType, object value)
    {
        // DuckDB expects INTERVAL as a formatted string, not ticks
        if (value is TimeSpan timeSpan)
        {
            // Format as "HH:MM:SS" which DuckDB can parse as INTERVAL
            return timeSpan.ToString(@"hh\:mm\:ss");
        }
        return value;
    }

    public override object FromDbValue(Type fieldType, object value)
    {
        // DuckDB returns INTERVAL as different types depending on the data
        if (value == null || value is DBNull)
        {
            return TimeSpan.Zero;
        }

        // Try to convert to long (microseconds)
        if (value is long microseconds)
        {
            return TimeSpan.FromTicks(microseconds * 10); // Convert microseconds to ticks
        }

        // If it's an int, convert to long first
        if (value is int intMicroseconds)
        {
            return TimeSpan.FromTicks((long)intMicroseconds * 10);
        }

        // Try parsing as TimeSpan if it's a string
        if (value is string str && TimeSpan.TryParse(str, out var timeSpan))
        {
            return timeSpan;
        }

        // Fallback: try to convert to ticks
        try
        {
            var ticks = Convert.ToInt64(value);
            return TimeSpan.FromTicks(ticks * 10); // Assume microseconds
        }
        catch
        {
            return TimeSpan.Zero;
        }
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

    public override object FromDbValue(Type fieldType, object value)
    {
        // DuckDB returns UnmanagedMemoryStream for BLOB data
        if (value is System.IO.UnmanagedMemoryStream stream)
        {
            using (stream)
            {
                var buffer = new byte[stream.Length];
                stream.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        return base.FromDbValue(fieldType, value);
    }
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
