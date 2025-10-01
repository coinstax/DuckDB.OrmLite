# CSV to Parquet Conversion Guide

This guide explains how to convert CSV files to Parquet format with year-based partitioning using the provided script.

## Quick Start

```bash
# Convert all fiatprice_*.csv files to Parquet, partitioned by year
./convert_csv_to_parquet.sh

# Or specify custom pattern and output directory
./convert_csv_to_parquet.sh "data_*.csv" "output_parquet"
```

## What the Script Does

1. **Reads all matching CSV files** - Uses glob pattern to find all CSV files
2. **Automatically detects CSV format** - Headers, delimiters, data types
3. **Partitions by year** - Creates subdirectories like `year=2024/`, `year=2025/`
4. **Compresses with Snappy** - Fast compression optimized for query performance
5. **Shows statistics** - Displays row counts, date ranges, compression ratios

## Output Structure

After running the script, you'll get a directory structure like:

```
fiatprice_parquet/
├── year=2023/
│   └── data_0.parquet
├── year=2024/
│   └── data_0.parquet
└── year=2025/
    └── data_0.parquet
```

Each subdirectory contains Parquet files for that specific year.

## Querying Partitioned Data

### Query Specific Year

```csharp
// Only reads files in year=2024/ directory
var prices2024 = db.SqlList<FiatPrice>(
    "SELECT * FROM 'fiatprice_parquet/year=2024/*.parquet'"
);
```

### Query Multiple Years

```csharp
// Reads all years
var allPrices = db.SqlList<FiatPrice>(
    "SELECT * FROM 'fiatprice_parquet/**/*.parquet'"
);
```

### Query with Predicate Pushdown

```csharp
// DuckDB only reads relevant partitions
var recentPrices = db.SqlList<FiatPrice>(@"
    SELECT * FROM 'fiatprice_parquet/**/*.parquet'
    WHERE Date >= '2024-01-01'
    AND Symbol = 'USD'
");
```

## Benefits of Partitioning by Year

1. **Faster queries** - DuckDB skips irrelevant year directories
2. **Better organization** - Easy to manage data by time period
3. **Incremental updates** - Add new year partitions without touching old ones
4. **Storage efficiency** - Archive old years to cheaper storage

## Advanced Usage

### Custom Date Column

If your CSV has a different date column name:

```bash
# Edit the script and change:
PARTITION_BY (year(Date))
# to:
PARTITION_BY (year(YourDateColumn))
```

### Different Partition Strategies

**By Year and Month:**
```sql
PARTITION_BY (year(Date), month(Date))
-- Creates: year=2024/month=01/, year=2024/month=02/, etc.
```

**By Symbol and Year:**
```sql
PARTITION_BY (Symbol, year(Date))
-- Creates: Symbol=USD/year=2024/, Symbol=EUR/year=2024/, etc.
```

### Compression Options

Edit the script to change compression:

```bash
COMPRESSION 'SNAPPY'  # Fast, good compression (default)
COMPRESSION 'GZIP'    # Better compression, slower
COMPRESSION 'ZSTD'    # Best compression, fast
```

## Troubleshooting

### No CSV files found
```
Error: No CSV files found matching pattern 'fiatprice_*.csv'
```
**Solution**: Check that CSV files exist in the current directory and match the pattern.

### Date column not found
```
Error: Binder Error: Referenced column "Date" not found
```
**Solution**: Ensure your CSV has a column named "Date" or modify the script to use your date column name.

### Permission denied
```
Permission denied: ./convert_csv_to_parquet.sh
```
**Solution**: Make the script executable: `chmod +x convert_csv_to_parquet.sh`

## Performance Tips

1. **Larger files = better compression** - Combine small CSVs before converting
2. **Keep partition directories balanced** - Avoid having too many tiny partitions
3. **Use appropriate row group size** - Default 100,000 works well for most cases
4. **Query with partition filters** - Always filter on partition columns when possible

## Comparison: CSV vs Parquet

Typical results for FiatPrice data:

| Format | Size | Query Time | Storage Cost |
|--------|------|------------|--------------|
| CSV (uncompressed) | 500 MB | 5.0s | High |
| CSV (gzipped) | 100 MB | 8.0s* | Medium |
| Parquet (SNAPPY) | 50 MB | 0.5s | Low |

*Gzipped CSV must be decompressed before reading

## Next Steps

After converting to Parquet, you can:

1. Query files directly without importing to database
2. Keep recent data in database, historical in Parquet files
3. Build data pipelines that write directly to Parquet
4. Archive old partitions to object storage (S3, etc.)

## Resources

- [DuckDB Parquet Documentation](https://duckdb.org/docs/data/parquet.html)
- [Apache Parquet Format](https://parquet.apache.org/)
- [DuckDB Partitioning](https://duckdb.org/docs/data/partitioning.html)
