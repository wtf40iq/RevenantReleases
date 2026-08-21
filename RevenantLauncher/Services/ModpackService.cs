using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    /// <summary>
    /// Экспорт и импорт сборок в формате Modrinth (.mrpack).
    /// Экспорт: моды с Modrinth попадают в индекс со ссылками, остальные — в overrides.
    /// Импорт: файлы скачиваются по ссылкам из индекса, overrides копируются как есть.
    /// </summary>
    public static class ModpackService
    {
        public class ExportResult
        {
            public int IndexedMods;
            public int OverrideMods;
        }

        public class ImportResult
        {
            public string PackName = "";
            public string McVersion = "";
            public string Loader = "";
            public int Downloaded;
            public int Overridden;
        }

        // ===== ЭКСПОРТ =====

        public static async Task<ExportResult> ExportMrpackAsync(string versionDisplayName, string destPath, string packName)
        {
            var result = new ExportResult();
            var (mcVer, loader) = PathService.ParseDisplayName(versionDisplayName);
            var loaderVersion = ParseLoaderVersion(versionDisplayName);

            var modsFolder = PathService.GetModsFolderFromDisplayName(versionDisplayName);
            var modFiles = Directory.Exists(modsFolder)
                ? Directory.GetFiles(modsFolder, "*.jar").ToList()
                : new List<string>();

            // Хэши → версии на Modrinth (для download-ссылок)
            var hashMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // hash -> fileName
            foreach (var f in modFiles)
            {
                var hash = ModService.ComputeSha1(f);
                if (hash != null) hashMap[hash] = Path.GetFileName(f);
            }

            var versionsByHash = hashMap.Count > 0
                ? await ModrinthService.Instance.GetVersionsByHashesAsync(hashMap.Keys)
                : new Dictionary<string, ModrinthVersion>();

            var indexFiles = new List<object>();
            var overrides = new List<(string source, string entryName)>();

            foreach (var file in modFiles)
            {
                var fileName = Path.GetFileName(file);
                var hash = hashMap.FirstOrDefault(kv => kv.Value == fileName).Key;

                if (hash != null && versionsByHash.TryGetValue(hash, out var ver) &&
                    !string.IsNullOrEmpty(ver.DownloadUrl))
                {
                    indexFiles.Add(new
                    {
                        path = $"mods/{fileName}",
                        hashes = new { sha1 = hash },
                        env = new { client = "required", server = "required" },
                        downloads = new[] { ver.DownloadUrl },
                        fileSize = new FileInfo(file).Length
                    });
                    result.IndexedMods++;
                }
                else
                {
                    overrides.Add((file, $"overrides/mods/{fileName}"));
                    result.OverrideMods++;
                }
            }

            var dependencies = new Dictionary<string, string> { ["minecraft"] = mcVer };
            if (!string.IsNullOrEmpty(loader) && loader != "vanilla")
            {
                var loaderKey = loader.ToLower() switch
                {
                    "fabric" => "fabric-loader",
                    "forge" => "forge",
                    "neoforge" => "neoforge",
                    "quilt" => "quilt-loader",
                    _ => loader.ToLower()
                };
                dependencies[loaderKey] = loaderVersion ?? "*";
            }

            var index = new
            {
                formatVersion = 1,
                game = "minecraft",
                versionId = DateTime.Now.ToString("yyyy.MM.dd-HH.mm"),
                name = packName,
                summary = "Сборка Revenant Launcher",
                files = indexFiles,
                dependencies
            };

            await Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(destPath)) File.Delete(destPath);

                using var zip = ZipFile.Open(destPath, ZipArchiveMode.Create);

                // modrinth.index.json
                var indexEntry = zip.CreateEntry("modrinth.index.json");
                using (var es = indexEntry.Open())
                {
                    var bytes = Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
                    es.Write(bytes, 0, bytes.Length);
                }

                // overrides
                foreach (var (source, entryName) in overrides)
                    zip.CreateEntryFromFile(source, entryName);
            });

            return result;
        }

        // ===== ИМПОРТ =====

        public static async Task<ImportResult> ImportMrpackAsync(string packPath, Action<string>? status = null)
        {
            var result = new ImportResult();

            string indexJson;
            var overrideEntries = new List<(string entryName, byte[] data)>();

            using (var zip = ZipFile.OpenRead(packPath))
            {
                var indexEntry = zip.GetEntry("modrinth.index.json")
                    ?? throw new InvalidOperationException("Это не .mrpack: нет modrinth.index.json");
                using (var reader = new StreamReader(indexEntry.Open()))
                    indexJson = reader.ReadToEnd();

                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase) &&
                        entry.Length > 0)
                    {
                        var rel = SanitizeArchivePath(entry.FullName.Substring("overrides/".Length));
                        if (rel == null)
                        {
                            // Путь-траверсал (../..\ и т.п.) — пропускаем, чтобы сборка
                            // не могла записать файлы за пределы папки игры
                            Console.WriteLine($"[Modpack] Пропущен опасный путь в overrides: {entry.FullName}");
                            continue;
                        }

                        using var ms = new MemoryStream();
                        entry.Open().CopyTo(ms);
                        overrideEntries.Add((rel, ms.ToArray()));
                    }
                }
            }

            using var doc = JsonDocument.Parse(indexJson);
            var root = doc.RootElement;

            result.PackName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            string mcVer = "";
            string loader = "";
            string loaderVersion = "";
            if (root.TryGetProperty("dependencies", out var deps))
            {
                foreach (var d in deps.EnumerateObject())
                {
                    switch (d.Name)
                    {
                        case "minecraft": mcVer = d.Value.GetString() ?? ""; break;
                        case "fabric-loader": loader = "fabric"; loaderVersion = d.Value.GetString() ?? ""; break;
                        case "forge": loader = "forge"; loaderVersion = d.Value.GetString() ?? ""; break;
                        case "neoforge": loader = "neoforge"; loaderVersion = d.Value.GetString() ?? ""; break;
                        case "quilt-loader": loader = "quilt"; loaderVersion = d.Value.GetString() ?? ""; break;
                    }
                }
            }

            result.McVersion = mcVer;
            result.Loader = loader;

            if (string.IsNullOrEmpty(mcVer))
                throw new InvalidOperationException("В сборке не указана версия Minecraft");

            // Целевая папка модов — под версию сборки
            var targetLoader = string.IsNullOrEmpty(loader) ? "vanilla" : loader;
            var modsFolder = PathService.GetModsFolderForVersion(mcVer, targetLoader);

            // Скачиваем файлы из индекса
            if (root.TryGetProperty("files", out var files))
            {
                foreach (var f in files.EnumerateArray())
                {
                    var path = f.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var url = "";
                    if (f.TryGetProperty("downloads", out var dl) && dl.GetArrayLength() > 0)
                        url = dl[0].GetString() ?? "";

                    if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url)) continue;

                    var fileName = Path.GetFileName(path);
                    status?.Invoke($"Скачивание {fileName}...");

                    try
                    {
                        using var stream = await ModrinthService.Instance.DownloadModAsync(url);
                        Directory.CreateDirectory(modsFolder);
                        using var fs = File.Create(Path.Combine(modsFolder, fileName));
                        await stream.CopyToAsync(fs);
                        result.Downloaded++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Modpack] download failed {fileName}: {ex.Message}");
                    }
                }
            }

            // Копируем overrides (mods/, resourcepacks/ и т.д.)
            var gameDir = string.IsNullOrEmpty(ConfigService.Instance.Data.Settings.GameDirectory)
                ? PathService.MinecraftDirectory
                : ConfigService.Instance.Data.Settings.GameDirectory;

            foreach (var (entryName, data) in overrideEntries)
            {
                var dest = Path.Combine(gameDir, entryName);
                var destDir = Path.GetDirectoryName(dest);
                if (string.IsNullOrEmpty(destDir)) continue;
                Directory.CreateDirectory(destDir);
                await File.WriteAllBytesAsync(dest, data);
                result.Overridden++;
            }

            return result;
        }

        /// <summary>
        /// Проверяет, что относительный путь из архива безопасен: без выхода за пределы
        /// целевой папки (".."), без корневых и дисковых путей, без недопустимых символов.
        /// Возвращает нормализованный относительный путь или null для опасного пути.
        /// </summary>
        private static string? SanitizeArchivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            // ZIP использует '/', но злонамеренный архив может содержать '\'
            // (на Windows это тоже разделитель пути)
            var normalized = path.Replace('\\', '/');

            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.StartsWith("./", StringComparison.Ordinal) ||
                normalized.Contains("//", StringComparison.Ordinal))
                return null;

            var segments = normalized.Split('/');
            var result = new List<string>();
            foreach (var seg in segments)
            {
                if (seg.Length == 0 || seg == ".") continue;
                if (seg == "..") return null; // попытка выйти из целевой папки
                if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                result.Add(seg);
            }

            if (result.Count == 0) return null;
            return string.Join(Path.DirectorySeparatorChar, result);
        }

        private static string? ParseLoaderVersion(string displayName)
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? parts[2] : null;
        }
    }
}
