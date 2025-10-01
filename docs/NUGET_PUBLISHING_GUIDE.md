# NuGet Publishing Guide

## Prerequisites

1. **NuGet Account**: Create an account at https://www.nuget.org/
2. **API Key**: Generate an API key from your NuGet account settings
   - Go to https://www.nuget.org/account/apikeys
   - Click "Create"
   - Give it a name (e.g., "DuckDB.OrmLite")
   - Select scopes: **Push new packages and package versions**
   - Select packages:
     - **Option A**: Choose "All" (simpler, less secure)
     - **Option B**: Choose "Glob pattern" and enter `DuckDB.OrmLite*` (more secure, recommended)
   - Click "Create"
   - **IMPORTANT**: Copy the API key immediately (you won't be able to see it again)

## Package Configuration

The package is configured in `src/DuckDB.OrmLite/DuckDB.OrmLite.csproj`:

```xml
<PropertyGroup>
  <PackageId>DuckDB.OrmLite</PackageId>
  <Version>1.0.0</Version>
  <Authors>CoinsTax LLC</Authors>
  <Description>DuckDB provider for ServiceStack.OrmLite...</Description>
  <PackageTags>ServiceStack;OrmLite;DuckDB;ORM;Database;Analytics;OLAP;DataProcessing</PackageTags>
  <PackageProjectUrl>https://github.com/coinstax/DuckDB.OrmLite</PackageProjectUrl>
  <RepositoryUrl>https://github.com/coinstax/DuckDB.OrmLite</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

## Building the Package

```bash
# Build in Release mode
dotnet build -c Release

# Create the NuGet package
dotnet pack src/DuckDB.OrmLite/DuckDB.OrmLite.csproj -c Release -o nupkg

# The package will be created at: nupkg/DuckDB.OrmLite.1.0.0.nupkg
```

## Publishing to NuGet

### Method 1: Using dotnet CLI (Recommended)

```bash
# Push the package with your API key
dotnet nuget push nupkg/DuckDB.OrmLite.1.0.0.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json

# Or if you saved your API key to a file:
dotnet nuget push nupkg/DuckDB.OrmLite.1.0.0.nupkg --api-key $(cat nuget-apikey.txt) --source https://api.nuget.org/v3/index.json
```

### Method 2: Using NuGet.org Web Interface

1. Go to https://www.nuget.org/packages/manage/upload
2. Click "Browse" and select your `.nupkg` file
3. Click "Upload"
4. Review the package metadata
5. Click "Submit"

## Version Management

Follow [Semantic Versioning](https://semver.org/):
- **Major** (1.0.0 → 2.0.0): Breaking changes
- **Minor** (1.0.0 → 1.1.0): New features, backward compatible
- **Patch** (1.0.0 → 1.0.1): Bug fixes, backward compatible

### When to Increment Each Version Component

**MAJOR version** - Increment when making incompatible API changes:
- Removing public methods/properties
- Changing method signatures
- Renaming namespaces or classes
- Changing behavior that breaks existing code

**MINOR version** - Increment when adding functionality in a backward-compatible manner:
- Adding new public methods/properties
- Adding new features
- Adding optional parameters with defaults
- Performance improvements (significant)

**PATCH version** - Increment when making backward-compatible bug fixes:
- Fixing bugs
- Documentation updates
- Internal refactoring (no API changes)
- Minor performance improvements

## Complete Release Workflow

Follow these steps for every new release:

### Step 1: Make Your Changes

Make code changes, fix bugs, or add features on your development branch.

### Step 2: Update Version and Release Notes

Edit `src/DuckDB.OrmLite/DuckDB.OrmLite.csproj`:

```xml
<PropertyGroup>
  <Version>1.0.1</Version>
  <PackageReleaseNotes>v1.0.1: Fixed NULL parameter handling; improved error messages</PackageReleaseNotes>
</PropertyGroup>
```

### Step 3: Update CHANGELOG.md

Add entry to `CHANGELOG.md`:

```markdown
## [1.0.1] - 2025-10-15

### Fixed
- Fixed NULL parameter handling in complex queries
- Improved error messages for connection failures

### Changed
- Updated DuckDB.NET.Data.Full to 1.3.1
```

### Step 4: Build and Test

```bash
# Clean previous builds
dotnet clean

# Build in Release mode
dotnet build -c Release

# Run all tests
dotnet test -c Release
```

### Step 5: Create NuGet Package

```bash
# Create the package
dotnet pack src/DuckDB.OrmLite/DuckDB.OrmLite.csproj -c Release -o nupkg

# Verify package contents (optional)
unzip -l nupkg/DuckDB.OrmLite.1.0.1.nupkg
```

### Step 6: Publish to NuGet

```bash
# Push to NuGet
dotnet nuget push nupkg/DuckDB.OrmLite.1.0.1.nupkg --api-key $(cat nuget-apikey.txt) --source https://api.nuget.org/v3/index.json

# Wait a few minutes for indexing
```

### Step 7: Commit and Push

```bash
# Commit version changes
git add src/DuckDB.OrmLite/DuckDB.OrmLite.csproj CHANGELOG.md
git commit -m "Release v1.0.1"
git push
```

### Step 8: Create Git Tag

```bash
# Create annotated tag
git tag -a v1.0.1 -m "Release v1.0.1 - Bug fixes

- Fixed NULL parameter handling
- Improved error messages"

# Push tag to GitHub
git push origin v1.0.1
```

### Step 9: Create GitHub Release

```bash
# Create GitHub release with notes
gh release create v1.0.1 \
  --title "v1.0.1 - Bug Fixes" \
  --notes "## What's Changed

### Fixed
- Fixed NULL parameter handling in complex queries
- Improved error messages for connection failures

### Changed
- Updated DuckDB.NET.Data.Full to 1.3.1

**Full Changelog**: https://github.com/coinstax/DuckDB.OrmLite/compare/v1.0.0...v1.0.1"
```

### Step 10: Verify

1. Check NuGet package page: https://www.nuget.org/packages/DuckDB.OrmLite
2. Check GitHub release page: https://github.com/coinstax/DuckDB.OrmLite/releases
3. Test installation:
   ```bash
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package DuckDB.OrmLite
   ```

## Quick Reference: Release Checklist

- [ ] Update version in `.csproj`
- [ ] Update `PackageReleaseNotes` in `.csproj`
- [ ] Update `CHANGELOG.md`
- [ ] Run tests: `dotnet test -c Release`
- [ ] Create package: `dotnet pack -c Release`
- [ ] Publish to NuGet: `dotnet nuget push`
- [ ] Commit changes: `git commit -m "Release vX.Y.Z"`
- [ ] Create tag: `git tag -a vX.Y.Z`
- [ ] Push: `git push && git push origin vX.Y.Z`
- [ ] Create GitHub release: `gh release create`
- [ ] Verify on NuGet.org and GitHub

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
unzip -l nupkg/DuckDB.OrmLite.1.0.0.nupkg
```

Check that it includes:
- ✅ `lib/net8.0/DuckDB.OrmLite.dll`
- ✅ `lib/net8.0/DuckDB.OrmLite.xml` (documentation)
- ✅ `README.md`
- ✅ Dependencies (ServiceStack.OrmLite, DuckDB.NET.Data.Full)

## After Publishing

1. **Wait for indexing**: It can take a few minutes for the package to appear on NuGet.org
2. **Test installation**: Try installing in a test project
   ```bash
   dotnet new console -n TestInstall
   cd TestInstall
   dotnet add package DuckDB.OrmLite
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
