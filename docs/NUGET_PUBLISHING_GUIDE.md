# NuGet Publishing Guide

## Prerequisites

1. **NuGet Account**: Create an account at https://www.nuget.org/
2. **API Key**: Generate an API key from your NuGet account settings
   - Go to https://www.nuget.org/account/apikeys
   - Click "Create"
   - Give it a name (e.g., "ServiceStack.OrmLite.DuckDb")
   - Select scopes (Push new packages and package versions)
   - Select packages (All or specific patterns)
   - Click "Create"
   - **IMPORTANT**: Copy the API key immediately (you won't be able to see it again)

## Package Configuration

The package is configured in `src/ServiceStack.OrmLite.DuckDb/ServiceStack.OrmLite.DuckDb.csproj`:

```xml
<PropertyGroup>
  <PackageId>ServiceStack.OrmLite.DuckDb</PackageId>
  <Version>1.0.0</Version>
  <Authors>Colin Mackie</Authors>
  <Description>DuckDB provider for ServiceStack.OrmLite...</Description>
  <PackageTags>ServiceStack;OrmLite;DuckDB;ORM;Database;Analytics;OLAP;DataProcessing</PackageTags>
  <PackageProjectUrl>https://github.com/cdmackie/ServiceStack.OrmLite.DuckDb</PackageProjectUrl>
  <RepositoryUrl>https://github.com/cdmackie/ServiceStack.OrmLite.DuckDb</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

## Building the Package

```bash
# Build in Release mode
dotnet build -c Release

# Create the NuGet package
dotnet pack src/ServiceStack.OrmLite.DuckDb/ServiceStack.OrmLite.DuckDb.csproj -c Release -o nupkg

# The package will be created at: nupkg/ServiceStack.OrmLite.DuckDb.1.0.0.nupkg
```

## Publishing to NuGet

### Method 1: Using dotnet CLI (Recommended)

```bash
# Set your API key (one-time setup)
dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org

# Push the package
dotnet nuget push nupkg/ServiceStack.OrmLite.DuckDb.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

### Method 2: Using NuGet.org Web Interface

1. Go to https://www.nuget.org/packages/manage/upload
2. Click "Browse" and select your `.nupkg` file
3. Click "Upload"
4. Review the package metadata
5. Click "Submit"

## Version Management

Update the version number in the `.csproj` file before releasing:

```xml
<Version>1.0.1</Version>
```

Follow [Semantic Versioning](https://semver.org/):
- **Major** (1.0.0 → 2.0.0): Breaking changes
- **Minor** (1.0.0 → 1.1.0): New features, backward compatible
- **Patch** (1.0.0 → 1.0.1): Bug fixes, backward compatible

## Pre-release Versions

For beta/alpha releases:

```xml
<Version>1.0.0-beta</Version>
<Version>1.0.0-alpha.1</Version>
```

## Verifying the Package

Before publishing, verify the package contents:

```bash
# Install NuGet Package Explorer (one-time)
dotnet tool install -g NuGetPackageExplorer

# Or use unzip to inspect
unzip -l nupkg/ServiceStack.OrmLite.DuckDb.1.0.0.nupkg
```

Check that it includes:
- ✅ `lib/net8.0/ServiceStack.OrmLite.DuckDb.dll`
- ✅ `lib/net8.0/ServiceStack.OrmLite.DuckDb.xml` (documentation)
- ✅ `README.md`
- ✅ Dependencies (ServiceStack.OrmLite, DuckDB.NET.Data.Full)

## After Publishing

1. **Wait for indexing**: It can take a few minutes for the package to appear on NuGet.org
2. **Test installation**: Try installing in a test project
   ```bash
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package ServiceStack.OrmLite.DuckDb
   ```
3. **Check package page**: Verify the README and metadata display correctly

## Unlisting a Package

If you need to remove a version from search results (you cannot delete packages):

1. Go to your package page on NuGet.org
2. Click "Manage Package"
3. Select the version
4. Click "Unlist"

**Note**: Unlisted packages can still be installed if the exact version is specified.

## Best Practices

1. **Test thoroughly** before publishing
2. **Update README.md** with any breaking changes
3. **Tag releases** in Git: `git tag v1.0.0 && git push --tags`
4. **Write release notes** on GitHub
5. **Keep dependencies updated** but be careful with breaking changes
6. **Monitor** the package page for issues and questions

## Troubleshooting

### Package already exists
- You cannot overwrite a published version
- Increment the version number

### API key not working
- Regenerate the API key from NuGet.org
- Make sure it has "Push" permissions
- Check that the API key hasn't expired

### Package won't upload
- Check file size (max 250MB)
- Verify the package isn't corrupted: `unzip -t nupkg/...`
- Try the web upload interface

## Resources

- [NuGet Documentation](https://docs.microsoft.com/en-us/nuget/)
- [Publishing Packages](https://docs.microsoft.com/en-us/nuget/nuget-org/publish-a-package)
- [Package Best Practices](https://docs.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
- [Semantic Versioning](https://semver.org/)
