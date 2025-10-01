using System;
using System.Data;
using System.IO;
using ServiceStack;
using ServiceStack.OrmLite;
using Xunit;

namespace DuckDbOrmLite.Tests;

/// <summary>
/// Global test fixture that sets up OrmLiteConfig.BeforeExecFilter once for all tests
/// This avoids race conditions when multiple test classes set the filter in their constructors
/// </summary>
public class TestFixture
{
    public TestFixture()
    {
        // Load ServiceStack license from .env file
        LoadServiceStackLicense();

        // Setup BeforeExecFilter once for ALL tests
        OrmLiteConfig.BeforeExecFilter = dbCmd =>
        {
            var sql = dbCmd.CommandText;

            foreach (IDbDataParameter param in dbCmd.Parameters)
            {
                if (param.ParameterName.StartsWith("$"))
                {
                    var nameWithoutPrefix = param.ParameterName.Substring(1);
                    if (int.TryParse(nameWithoutPrefix, out int index))
                    {
                        var newSqlParam = $"${index + 1}";
                        sql = sql.Replace(param.ParameterName, newSqlParam);
                        param.ParameterName = (index + 1).ToString();
                    }
                    else
                    {
                        param.ParameterName = nameWithoutPrefix;
                    }
                }
            }

            dbCmd.CommandText = sql;
        };
    }

    private void LoadServiceStackLicense()
    {
        try
        {
            // Look for .env file in project root (2 levels up from bin/Debug/net8.0)
            var envPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "..",
                ".env");

            envPath = Path.GetFullPath(envPath);

            if (File.Exists(envPath))
            {
                var lines = File.ReadAllLines(envPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Trim() == "SERVICESTACK_LICENSE")
                    {
                        var license = parts[1].Trim();
                        Licensing.RegisterLicense(license);
                        Console.WriteLine("ServiceStack license loaded from .env");
                        return;
                    }
                }
            }
            else
            {
                Console.WriteLine($".env file not found at: {envPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not load ServiceStack license: {ex.Message}");
        }
    }
}

[CollectionDefinition("DuckDB Tests", DisableParallelization = false)]
public class TestCollection : ICollectionFixture<TestFixture>
{
    // This class has no code, and is never created.
    // Its purpose is simply to be the place to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}
