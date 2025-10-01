using System.Data;
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
}

[CollectionDefinition("DuckDB Tests", DisableParallelization = false)]
public class TestCollection : ICollectionFixture<TestFixture>
{
    // This class has no code, and is never created.
    // Its purpose is simply to be the place to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}
