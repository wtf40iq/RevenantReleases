using RevenantLauncher.Services;
using Xunit;

namespace RevenantLauncher.Tests;

public class PathServiceTests
{
    [Theory]
    [InlineData("1.21.11", "1.21.11", "vanilla")]
    [InlineData("1.21.11 Fabric 0.16.14", "1.21.11", "fabric")]
    [InlineData("1.20.1 Forge 47.2.0", "1.20.1", "forge")]
    [InlineData("1.19.2 Quilt 0.24.1", "1.19.2", "quilt")]
    [InlineData("1.18.2 NeoForge 1.0.0", "1.18.2", "neoforge")]
    [InlineData("", "1.20.1", "vanilla")]
    [InlineData(null, "1.20.1", "vanilla")]
    public void ParseDisplayName_Cases(string? input, string expectedMc, string expectedLoader)
    {
        var (mc, loader) = PathService.ParseDisplayName(input ?? "");
        Assert.Equal(expectedMc, mc);
        Assert.Equal(expectedLoader, loader);
    }
}

public class ModServiceTests
{
    [Theory]
    [InlineData("1.21.11 Fabric", "1.21.11-fabric")]
    [InlineData("1.20.1 Forge 47.2.0", "1.20.1-forge")]
    [InlineData("1.16.5", "1.16.5-vanilla")]
    public void FolderKeyFromDisplayName(string display, string expected)
        => Assert.Equal(expected, ModService.FolderKeyFromDisplayName(display));

    [Theory]
    [InlineData("1.21.11-fabric", "1.21.11 Fabric")]
    [InlineData("1.20.1-vanilla", "1.20.1 Vanilla")]
    [InlineData("weird", "weird")]
    public void PrettifyFolderKey(string key, string expected)
        => Assert.Equal(expected, ModService.PrettifyFolderKey(key));
}
