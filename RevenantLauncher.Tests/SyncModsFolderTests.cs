using System.IO;
using RevenantLauncher.Services;
using Xunit;

namespace RevenantLauncher.Tests;

/// <summary>
/// Синхронизация модов (MinecraftService.SyncModsFolder) должна удалять только
/// файлы, скопированные ею ранее (по манифесту .revenant_synced), и никогда —
/// моды, добавленные пользователем вручную.
/// </summary>
public class SyncModsFolderTests : IDisposable
{
    private readonly string _source;
    private readonly string _dest;

    public SyncModsFolderTests()
    {
        _source = Path.Combine(Path.GetTempPath(), "rev-sync-src-" + Guid.NewGuid().ToString("N"));
        _dest = Path.Combine(Path.GetTempPath(), "rev-sync-dst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_dest);
    }

    public void Dispose()
    {
        try { Directory.Delete(_source, true); } catch { }
        try { Directory.Delete(_dest, true); } catch { }
    }

    private void WriteMod(string folder, string name, string content = "jar-bytes")
        => File.WriteAllText(Path.Combine(folder, name), content);

    private static string[] Files(string dir)
        => Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x).ToArray()!;

    [Fact]
    public void Sync_CopiesSourceAndWritesManifest()
    {
        WriteMod(_source, "mod-a.jar");
        WriteMod(_source, "mod-b.jar");

        MinecraftService.SyncModsFolder(_source, _dest);

        Assert.Contains("mod-a.jar", Files(_dest));
        Assert.Contains("mod-b.jar", Files(_dest));
        Assert.Contains(".revenant_synced", Files(_dest));
    }

    [Fact]
    public void Sync_DoesNotDeleteUserAddedFiles()
    {
        WriteMod(_source, "mod-a.jar");
        MinecraftService.SyncModsFolder(_source, _dest);

        // Пользователь вручную кладёт мод в общую папку
        WriteMod(_dest, "user-mod.jar");

        MinecraftService.SyncModsFolder(_source, _dest);

        Assert.Contains("mod-a.jar", Files(_dest));
        Assert.Contains("user-mod.jar", Files(_dest)); // должен выжить!
    }

    [Fact]
    public void Sync_DeletesOnlyPreviouslySyncedFilesRemovedFromSource()
    {
        WriteMod(_source, "mod-a.jar");
        WriteMod(_source, "mod-b.jar");
        MinecraftService.SyncModsFolder(_source, _dest);

        // Пользователь добавил свой мод; из исходной папки удалили mod-b.jar
        WriteMod(_dest, "user-mod.jar");
        File.Delete(Path.Combine(_source, "mod-b.jar"));

        MinecraftService.SyncModsFolder(_source, _dest);

        Assert.Contains("mod-a.jar", Files(_dest));
        Assert.DoesNotContain("mod-b.jar", Files(_dest)); // наш прошлый файл — удалён
        Assert.Contains("user-mod.jar", Files(_dest));    // чужой — остался
    }

    [Fact]
    public void Sync_DisabledModsAreTrackedAndReplaced()
    {
        WriteMod(_source, "mod-a.jar");
        MinecraftService.SyncModsFolder(_source, _dest);

        // Мод отключён в папке версии (переименован в .disabled)
        File.Delete(Path.Combine(_source, "mod-a.jar"));
        WriteMod(_source, "mod-a.jar.disabled");

        MinecraftService.SyncModsFolder(_source, _dest);

        Assert.Contains("mod-a.jar.disabled", Files(_dest));
        Assert.DoesNotContain("mod-a.jar", Files(_dest));
    }
}
