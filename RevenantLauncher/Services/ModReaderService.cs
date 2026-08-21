using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RevenantLauncher.Models;
using Tomlyn;

namespace RevenantLauncher.Services
{
    public static class ModReaderService
    {
        public static async Task<ModInfo> ReadAsync(string filePath)
        {
            var info = new ModInfo
            {
                FilePath = filePath,
                Name = Path.GetFileNameWithoutExtension(filePath),
                IsEnabled = !filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase),
            };

            try
            {
                info.FileSizeBytes = new FileInfo(filePath).Length;
            }
            catch { }

            // Проверяем кеш иконки на диске
            var cacheKey = GetCacheKey(filePath, info.FileSizeBytes);
            var cachedIconPath = Path.Combine(PathService.IconCacheDirectory, cacheKey + ".png");
            var cachedMetaPath = Path.Combine(PathService.IconCacheDirectory, cacheKey + ".json");

            byte[]? iconBytes = null;
            bool loadedFromCache = false;

            // Читаем meta из кеша если есть
            if (File.Exists(cachedMetaPath))
            {
                try
                {
                    var metaJson = await File.ReadAllTextAsync(cachedMetaPath);
                    var meta = JsonSerializer.Deserialize<CachedModMeta>(metaJson);
                    if (meta != null)
                    {
                        info.ModId = meta.ModId ?? "";
                        info.Name = meta.Name ?? info.Name;
                        info.Version = meta.Version ?? "";
                        info.Description = meta.Description ?? "";
                        info.Authors = meta.Authors ?? "";
                        info.Homepage = meta.Homepage ?? "";
                        info.Loader = meta.Loader;

                        if (File.Exists(cachedIconPath))
                        {
                            iconBytes = await File.ReadAllBytesAsync(cachedIconPath);
                        }
                        loadedFromCache = true;
                    }
                }
                catch { }
            }

            // Если из кеша не загрузили — парсим jar
            if (!loadedFromCache)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        using var zip = ZipFile.OpenRead(filePath);

                        var fabricEntry = zip.GetEntry("fabric.mod.json");
                        if (fabricEntry != null)
                        {
                            iconBytes = ParseFabric(zip, fabricEntry, info);
                            return;
                        }

                        var quiltEntry = zip.GetEntry("quilt.mod.json");
                        if (quiltEntry != null)
                        {
                            iconBytes = ParseQuilt(zip, quiltEntry, info);
                            return;
                        }

                        var neoforgeEntry = zip.GetEntry("META-INF/neoforge.mods.toml");
                        if (neoforgeEntry != null)
                        {
                            iconBytes = ParseForgeToml(zip, neoforgeEntry, info, ModLoader.NeoForge);
                            return;
                        }

                        var modsToml = zip.GetEntry("META-INF/mods.toml");
                        if (modsToml != null)
                        {
                            iconBytes = ParseForgeToml(zip, modsToml, info, ModLoader.Forge);
                            return;
                        }

                        var mcmodInfo = zip.GetEntry("mcmod.info");
                        if (mcmodInfo != null)
                        {
                            iconBytes = ParseMcModInfo(zip, mcmodInfo, info);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ModReader] Failed to read {filePath}: {ex.Message}");
                    }
                });

                // Сохраняем в кеш
                try
                {
                    var meta = new CachedModMeta
                    {
                        ModId = info.ModId,
                        Name = info.Name,
                        Version = info.Version,
                        Description = info.Description,
                        Authors = info.Authors,
                        Homepage = info.Homepage,
                        Loader = info.Loader
                    };
                    var metaJson = JsonSerializer.Serialize(meta);
                    await File.WriteAllTextAsync(cachedMetaPath, metaJson);

                    if (iconBytes != null && iconBytes.Length > 0)
                        await File.WriteAllBytesAsync(cachedIconPath, iconBytes);
                }
                catch { }
            }

            // Создаём Bitmap только на UI-потоке
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

        private static string GetCacheKey(string filePath, long fileSize)
        {
            var input = $"{Path.GetFileName(filePath)}|{fileSize}";
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).Substring(0, 16);
        }

        private class CachedModMeta
        {
            public string? ModId { get; set; }
            public string? Name { get; set; }
            public string? Version { get; set; }
            public string? Description { get; set; }
            public string? Authors { get; set; }
            public string? Homepage { get; set; }
            public ModLoader Loader { get; set; }
        }

        private static byte[]? ParseFabric(ZipArchive zip, ZipArchiveEntry entry, ModInfo info)
        {
            info.Loader = ModLoader.Fabric;
            byte[]? iconBytes = null;

            try
            {
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("id", out var id)) info.ModId = id.GetString() ?? "";
                if (root.TryGetProperty("name", out var name)) info.Name = name.GetString() ?? info.Name;
                if (root.TryGetProperty("version", out var ver)) info.Version = ver.GetString() ?? "";
                if (root.TryGetProperty("description", out var desc)) info.Description = desc.GetString() ?? "";

                if (root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var a in authors.EnumerateArray())
                    {
                        if (a.ValueKind == JsonValueKind.String) list.Add(a.GetString() ?? "");
                        else if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n))
                            list.Add(n.GetString() ?? "");
                    }
                    info.Authors = string.Join(", ", list);
                }

                if (root.TryGetProperty("contact", out var contact) &&
                    contact.TryGetProperty("homepage", out var home))
                    info.Homepage = home.GetString() ?? "";

                if (root.TryGetProperty("icon", out var icon))
                {
                    var iconPath = icon.GetString();
                    if (!string.IsNullOrEmpty(iconPath))
                        iconBytes = LoadIconBytesFromZip(zip, iconPath);
                }
            }
            catch { }

            return iconBytes;
        }

        private static byte[]? ParseQuilt(ZipArchive zip, ZipArchiveEntry entry, ModInfo info)
        {
            info.Loader = ModLoader.Quilt;
            byte[]? iconBytes = null;

            try
            {
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                if (root.TryGetProperty("quilt_loader", out var loader))
                {
                    if (loader.TryGetProperty("id", out var id)) info.ModId = id.GetString() ?? "";
                    if (loader.TryGetProperty("version", out var ver)) info.Version = ver.GetString() ?? "";

                    if (loader.TryGetProperty("metadata", out var meta))
                    {
                        if (meta.TryGetProperty("name", out var name)) info.Name = name.GetString() ?? info.Name;
                        if (meta.TryGetProperty("description", out var desc)) info.Description = desc.GetString() ?? "";

                        if (meta.TryGetProperty("icon", out var icon))
                        {
                            var iconPath = icon.GetString();
                            if (!string.IsNullOrEmpty(iconPath))
                                iconBytes = LoadIconBytesFromZip(zip, iconPath);
                        }
                    }
                }
            }
            catch { }

            return iconBytes;
        }

        private static byte[]? ParseForgeToml(ZipArchive zip, ZipArchiveEntry entry, ModInfo info, ModLoader loader)
        {
            info.Loader = loader;
            byte[]? iconBytes = null;

            try
            {
                using var reader = new StreamReader(entry.Open());
                var content = reader.ReadToEnd();

                var model = Toml.ToModel(content);
                if (model.TryGetValue("mods", out var modsObj) &&
                    modsObj is Tomlyn.Model.TomlTableArray modsArr &&
                    modsArr.Count > 0)
                {
                    var mod = modsArr[0];
                    if (mod.TryGetValue("modId", out var modId)) info.ModId = modId?.ToString() ?? "";
                    if (mod.TryGetValue("displayName", out var name)) info.Name = name?.ToString() ?? info.Name;
                    if (mod.TryGetValue("version", out var ver)) info.Version = ver?.ToString() ?? "";
                    if (mod.TryGetValue("description", out var desc)) info.Description = (desc?.ToString() ?? "").Trim();
                    if (mod.TryGetValue("authors", out var authors)) info.Authors = authors?.ToString() ?? "";
                    if (mod.TryGetValue("displayURL", out var url)) info.Homepage = url?.ToString() ?? "";

                    if (mod.TryGetValue("logoFile", out var logo))
                    {
                        var logoPath = logo?.ToString();
                        if (!string.IsNullOrEmpty(logoPath))
                            iconBytes = LoadIconBytesFromZip(zip, logoPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModReader] TOML parse failed: {ex.Message}");
            }

            return iconBytes;
        }

        private static byte[]? ParseMcModInfo(ZipArchive zip, ZipArchiveEntry entry, ModInfo info)
        {
            info.Loader = ModLoader.Forge;
            byte[]? iconBytes = null;

            try
            {
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                JsonElement modElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    modElement = root[0];
                else if (root.TryGetProperty("modList", out var modList) && modList.GetArrayLength() > 0)
                    modElement = modList[0];
                else
                    return null;

                if (modElement.TryGetProperty("modid", out var id)) info.ModId = id.GetString() ?? "";
                if (modElement.TryGetProperty("name", out var name)) info.Name = name.GetString() ?? info.Name;
                if (modElement.TryGetProperty("version", out var ver)) info.Version = ver.GetString() ?? "";
                if (modElement.TryGetProperty("description", out var desc)) info.Description = desc.GetString() ?? "";
                if (modElement.TryGetProperty("url", out var url)) info.Homepage = url.GetString() ?? "";

                if (modElement.TryGetProperty("authorList", out var authors) && authors.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var a in authors.EnumerateArray())
                        list.Add(a.GetString() ?? "");
                    info.Authors = string.Join(", ", list);
                }

                if (modElement.TryGetProperty("logoFile", out var logo))
                {
                    var logoPath = logo.GetString();
                    if (!string.IsNullOrEmpty(logoPath))
                        iconBytes = LoadIconBytesFromZip(zip, logoPath.TrimStart('/'));
                }
            }
            catch { }

            return iconBytes;
        }

        private static byte[]? LoadIconBytesFromZip(ZipArchive zip, string path)
        {
            try
            {
                var normalizedPath = path.TrimStart('/');
                var entry = zip.GetEntry(normalizedPath);
                if (entry == null) return null;

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}