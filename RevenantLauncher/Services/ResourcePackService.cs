using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class ResourcePackService
    {
        private static readonly Lazy<ResourcePackService> _instance = new(() => new ResourcePackService());
        public static ResourcePackService Instance => _instance.Value;

        public ObservableCollection<ResourcePackInfo> Packs { get; } = new();

        private static string InstalledMapFile =>
            Path.Combine(PathService.MinecraftDirectory, "installed_resourcepacks.json");

        private Dictionary<string, string> _installedMap = new();

        private ResourcePackService()
        {
            LoadInstalledMap();
        }

        public async Task LoadAsync()
        {
            var folder = PathService.ResourcePacksDirectory;
            Directory.CreateDirectory(folder);

            var files = Directory.GetFiles(folder, "*.zip")
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            var readTasks = files.Select(ReadAsync).ToArray();
            var infos = await Task.WhenAll(readTasks);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Packs.Clear();
                foreach (var info in infos)
                    Packs.Add(info);
            });

            Console.WriteLine($"[ResourcePacks] Loaded {infos.Length} packs");
        }

        /// <summary>Читает pack.mcmeta (описание) и pack.png (иконку) из архива</summary>
        private static async Task<ResourcePackInfo> ReadAsync(string filePath)
        {
            var info = new ResourcePackInfo
            {
                FilePath = filePath,
                Name = Path.GetFileNameWithoutExtension(filePath)
            };

            try { info.FileSizeBytes = new FileInfo(filePath).Length; } catch { }

            byte[]? iconBytes = null;

            await Task.Run(() =>
            {
                try
                {
                    using var zip = ZipFile.OpenRead(filePath);

                    var metaEntry = zip.GetEntry("pack.mcmeta");
                    if (metaEntry != null)
                    {
                        using var stream = metaEntry.Open();
                        using var doc = JsonDocument.Parse(stream);
                        if (doc.RootElement.TryGetProperty("pack", out var pack))
                        {
                            if (pack.TryGetProperty("pack_format", out var fmt) && fmt.ValueKind == JsonValueKind.Number)
                                info.PackFormat = fmt.GetInt32();
                            if (pack.TryGetProperty("description", out var desc))
                                info.Description = FlattenTextComponent(desc);
                        }
                    }

                    var iconEntry = zip.GetEntry("pack.png");
                    if (iconEntry != null)
                    {
                        using var stream = iconEntry.Open();
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        iconBytes = ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ResourcePacks] Failed to read {filePath}: {ex.Message}");
                }
            });

            if (iconBytes != null && iconBytes.Length > 0)
            {
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            using var ms = new MemoryStream(iconBytes);
                            info.Icon = new Bitmap(ms);
                        }
                        catch { }
                    });
                }
                catch { }
            }

            return info;
        }

        /// <summary>Описание в pack.mcmeta — строка, объект {"text":...} или массив компонентов</summary>
        private static string FlattenTextComponent(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    return el.GetString() ?? "";
                case JsonValueKind.Object:
                    return el.TryGetProperty("text", out var t) ? FlattenTextComponent(t) : "";
                case JsonValueKind.Array:
                    return string.Concat(el.EnumerateArray().Select(FlattenTextComponent));
                default:
                    return el.ToString();
            }
        }

        public async Task<ResourcePackInfo> AddFromStreamAsync(Stream stream, string fileName, string? modrinthProjectId = null)
        {
            var folder = PathService.ResourcePacksDirectory;
            Directory.CreateDirectory(folder);

            var destPath = Path.Combine(folder, fileName);
            int counter = 1;
            while (File.Exists(destPath))
            {
                var name = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                destPath = Path.Combine(folder, $"{name}_{counter}{ext}");
                counter++;
            }

            using (var fs = File.Create(destPath))
                await stream.CopyToAsync(fs);

            if (!string.IsNullOrEmpty(modrinthProjectId))
                RegisterInstalled(Path.GetFileName(destPath), modrinthProjectId);

            var info = await ReadAsync(destPath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Packs.Any(p => p.FilePath == info.FilePath))
                    Packs.Add(info);
            });
            return info;
        }

        public async Task AddPackAsync(string sourceFilePath)
        {
            using var fs = File.OpenRead(sourceFilePath);
            await AddFromStreamAsync(fs, Path.GetFileName(sourceFilePath));
        }

        public async Task RemoveAsync(ResourcePackInfo pack)
        {
            var fileName = Path.GetFileName(pack.FilePath);
            await Task.Run(() =>
            {
                if (File.Exists(pack.FilePath))
                    File.Delete(pack.FilePath);
            });
            UnregisterInstalled(fileName);
            await Dispatcher.UIThread.InvokeAsync(() => Packs.Remove(pack));
        }

        /// <summary>SHA1-хэш содержимого файла (для проверки обновлений на Modrinth)</summary>
        public static string? ComputeSha1(string filePath)
        {
            try
            {
                using var sha = System.Security.Cryptography.SHA1.Create();
                using var stream = File.OpenRead(filePath);
                return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResourcePacks] SHA1 failed for {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Заменяет файл ресурспака на новую версию</summary>
        public async Task<ResourcePackInfo?> ReplacePackAsync(ResourcePackInfo old, Stream stream, string newFileName, string? projectId)
        {
            var folder = PathService.ResourcePacksDirectory;
            var oldFileName = Path.GetFileName(old.FilePath);

            string? newFilePath = null;
            await Task.Run(() =>
            {
                if (File.Exists(old.FilePath))
                    File.Delete(old.FilePath);

                var destPath = Path.Combine(folder, newFileName);
                int counter = 1;
                while (File.Exists(destPath))
                {
                    var name = Path.GetFileNameWithoutExtension(newFileName);
                    var ext = Path.GetExtension(newFileName);
                    destPath = Path.Combine(folder, $"{name}_{counter}{ext}");
                    counter++;
                }

                using (var fs = File.Create(destPath))
                    stream.CopyTo(fs);

                newFilePath = destPath;
            });

            if (newFilePath == null) return null;

            UnregisterInstalled(oldFileName);
            if (!string.IsNullOrEmpty(projectId))
                RegisterInstalled(Path.GetFileName(newFilePath), projectId);

            var info = await ReadAsync(newFilePath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Packs.Remove(old);
                if (!Packs.Any(p => p.FilePath == info.FilePath))
                    Packs.Add(info);
            });
            return info;
        }

        public async Task<bool> RemoveByProjectIdAsync(string projectId)
        {
            var fileName = _installedMap.FirstOrDefault(kvp =>
                kvp.Value.Equals(projectId, StringComparison.OrdinalIgnoreCase)).Key;

            if (string.IsNullOrEmpty(fileName))
                return false;

            var pack = Packs.FirstOrDefault(p =>
                Path.GetFileName(p.FilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (pack != null)
            {
                await RemoveAsync(pack);
                return true;
            }

            var fullPath = Path.Combine(PathService.ResourcePacksDirectory, fileName);
            var deleted = false;
            try
            {
                if (File.Exists(fullPath)) { File.Delete(fullPath); deleted = true; }
            }
            catch { }

            UnregisterInstalled(fileName);
            return deleted;
        }

        public bool IsProjectInstalled(string projectId, string? slug = null)
        {
            if (!string.IsNullOrEmpty(projectId))
            {
                var fileName = _installedMap.FirstOrDefault(kvp =>
                    kvp.Value.Equals(projectId, StringComparison.OrdinalIgnoreCase)).Key;

                if (!string.IsNullOrEmpty(fileName) &&
                    File.Exists(Path.Combine(PathService.ResourcePacksDirectory, fileName)))
                    return true;
            }

            if (!string.IsNullOrEmpty(slug))
            {
                var folder = PathService.ResourcePacksDirectory;
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.GetFiles(folder, "*.zip"))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (MatchesSlug(name, slug))
                        {
                            if (!string.IsNullOrEmpty(projectId))
                                RegisterInstalled(Path.GetFileName(file), projectId);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool MatchesSlug(string baseName, string slug)
        {
            return baseName.Equals(slug, StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith(slug + "-", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith(slug + "_", StringComparison.OrdinalIgnoreCase);
        }

        public void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(PathService.ResourcePacksDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PathService.ResourcePacksDirectory) { UseShellExecute = true });
            }
            catch { }
        }

        // ===== installed_resourcepacks.json =====

        private void RegisterInstalled(string fileName, string projectId)
        {
            _installedMap[fileName] = projectId;
            SaveInstalledMap();
        }

        private void UnregisterInstalled(string fileName)
        {
            if (_installedMap.Remove(fileName))
                SaveInstalledMap();
        }

        private void LoadInstalledMap()
        {
            try
            {
                if (!File.Exists(InstalledMapFile))
                {
                    _installedMap = new();
                    return;
                }

                var json = File.ReadAllText(InstalledMapFile);
                _installedMap = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch
            {
                _installedMap = new();
            }
        }

        private void SaveInstalledMap()
        {
            try
            {
                var dir = Path.GetDirectoryName(InstalledMapFile);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_installedMap, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(InstalledMapFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResourcePacks] SaveInstalledMap failed: {ex.Message}");
            }
        }
    }
}
