#!/bin/bash

# Convert CSV files to Parquet with partitioning by year
# Usage: ./convert_csv_to_parquet.sh [pattern] [output_dir]
# Example: ./convert_csv_to_parquet.sh "fiatprice_*.csv" "fiatprice_parquet"

# Get parameters or use defaults
CSV_PATTERN="${1:-fiatprice_*.csv}"
OUTPUT_DIR="${2:-fiatprice_parquet}"

echo "Converting CSV files matching: $CSV_PATTERN"
echo "Output directory: $OUTPUT_DIR"
echo ""

# Check if any CSV files match the pattern
if ! ls $CSV_PATTERN 1> /dev/null 2>&1; then
    echo "Error: No CSV files found matching pattern '$CSV_PATTERN'"
    exit 1
fi

# Create a temporary DuckDB database for the conversion
TEMP_DB=".temp_conversion.duckdb"

# Clean up temp database if it exists
rm -f "$TEMP_DB"

echo "Reading all CSV files and partitioning by year..."

# Use DuckDB to read all CSVs and partition by year
duckdb "$TEMP_DB" <<EOF
-- Read all CSV files matching the pattern
-- Match FiatPrice table schema exactly
CREATE TABLE temp_data AS
SELECT
    column0::TIMESTAMP as "Date",
    column1::VARCHAR as "Symbol",
    column2::DECIMAL(38,6) as "USD"
FROM read_csv_auto('$CSV_PATTERN',
    auto_detect=true
);

-- Show summary
SELECT
    COUNT(*) as total_rows,
    MIN("Date") as earliest_date,
    MAX("Date") as latest_date,
    COUNT(DISTINCT "Symbol") as unique_symbols
FROM temp_data;

-- Export to Parquet with year partitioning
COPY (
    SELECT
        "Date",
        "Symbol",
        "USD",
        year("Date"::TIMESTAMP) as year
    FROM temp_data
)
TO '$OUTPUT_DIR' (
    FORMAT PARQUET,
    PARTITION_BY (year),
    COMPRESSION 'SNAPPY',
    ROW_GROUP_SIZE 100000
);

-- Show partition summary
SELECT
    year("Date") as year,
    COUNT(*) as rows,
    COUNT(DISTINCT "Symbol") as symbols
FROM temp_data
GROUP BY year("Date")
ORDER BY year;
EOF

# Check if conversion was successful
if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Conversion complete!"
    echo ""
    echo "Output structure:"
    find "$OUTPUT_DIR" -type f -name "*.parquet" | sort
    echo ""
    echo "Total size:"
    du -sh "$OUTPUT_DIR"
    echo ""

    # Get CSV total size for comparison
    CSV_SIZE=$(du -ch $CSV_PATTERN 2>/dev/null | tail -1 | cut -f1)
    echo "Original CSV size: $CSV_SIZE"

    # Clean up temp database
    rm -f "$TEMP_DB"
else
    echo ""
    echo "❌ Error during conversion"
    rm -f "$TEMP_DB"
    exit 1
fi
