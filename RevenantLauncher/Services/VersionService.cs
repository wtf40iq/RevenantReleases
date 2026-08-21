using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class VersionService
    {
        private static readonly Lazy<VersionService> _instance = new(() => new VersionService());
        public static VersionService Instance => _instance.Value;

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private const string MojangManifest = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
        private const string FabricMcVersions = "https://meta.fabricmc.net/v2/versions/game";
        private const string FabricLoaderApi = "https://meta.fabricmc.net/v2/versions/loader";
        private const string ForgeMavenMeta = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

        public ObservableCollection<GameVersion> AvailableVersions { get; } = new();

        public List<GameVersion> VanillaVersions { get; private set; } = new();
        public List<GameVersion> ForgeVersions { get; private set; } = new();
        public List<GameVersion> FabricVersions { get; private set; } = new();

        private readonly Dictionary<string, List<string>> _fabricLoaderCache = new();
        private readonly Dictionary<string, List<string>> _forgeVersionCache = new();
        private List<string>? _allForgeVersions;

        public bool IsLoaded { get; private set; }

        /// <summary>Список версий загружен (или обновлён из сети) — можно обновить UI</summary>
        public event Action? VersionsLoaded;

        private VersionService() { }

        public async Task LoadAsync()
        {
            try
            {
                await LoadVanillaAsync();
                await LoadFabricSupportedAsync();
                await LoadForgeSupportedAsync();

                AvailableVersions.Clear();
                foreach (var v in VanillaVersions.Concat(ForgeVersions).Concat(FabricVersions))
                    AvailableVersions.Add(v);

                IsLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VersionService] Load failed: " + ex.Message);
                LoadDefaultsFallback();
            }
            finally
            {
                VersionsLoaded?.Invoke();
            }
        }

        private async Task LoadVanillaAsync()
        {
            var json = await _http.GetStringAsync(MojangManifest);
            using var doc = JsonDocument.Parse(json);

            var list = new List<GameVersion>();
            var versions = doc.RootElement.GetProperty("versions");

            foreach (var v in versions.EnumerateArray())
            {
                var id = v.GetProperty("id").GetString() ?? "";
                var type = v.GetProperty("type").GetString() ?? "release";

                list.Add(new GameVersion
                {
                    Id = id,
                    DisplayName = id,
                    Type = type switch
                    {
                        "release" => VersionType.Release,
                        "snapshot" => VersionType.Snapshot,
                        "old_beta" => VersionType.OldBeta,
                        "old_alpha" => VersionType.OldAlpha,
                        _ => VersionType.Release
                    }
                });
            }

            VanillaVersions = list;
        }

        private async Task LoadFabricSupportedAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(FabricMcVersions);
                using var doc = JsonDocument.Parse(json);

                var list = new List<GameVersion>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var version = item.GetProperty("version").GetString() ?? "";
                    var stable = item.TryGetProperty("stable", out var s) && s.GetBoolean();
                    if (!stable) continue;

                    list.Add(new GameVersion
                    {
                        Id = $"{version}-fabric",
                        DisplayName = version,
                        Type = VersionType.Fabric
                    });
                }
                FabricVersions = list;
            }
            catch
            {
                FabricVersions = new();
            }
        }

        /// <summary>
        /// Запасной список Forge-совместимых версий на случай, если maven-metadata недоступен.
        /// Основной путь — вычисление из реального списка версий Forge (см. LoadForgeSupportedAsync).
        /// </summary>
        private static readonly HashSet<string> FallbackForgeCompatible = new()
        {
            "1.21.5","1.21.4","1.21.3","1.21.1","1.21",
            "1.20.6","1.20.4","1.20.2","1.20.1","1.20",
            "1.19.4","1.19.3","1.19.2","1.19.1","1.19",
            "1.18.2","1.18.1","1.18",
            "1.17.1",
            "1.16.5","1.16.4","1.16.3","1.16.2","1.16.1","1.16",
            "1.15.2","1.15.1","1.15",
            "1.14.4","1.14.3","1.14.2",
            "1.13.2",
            "1.12.2","1.12.1","1.12",
            "1.11.2","1.11",
            "1.10.2","1.10",
            "1.9.4","1.9",
            "1.8.9","1.8.8","1.8",
            "1.7.10","1.7.2",
            "1.6.4",
            "1.5.2"
        };

        private async Task LoadForgeSupportedAsync()
        {
            // Получаем реальный список всех версий Forge из maven-metadata
            await EnsureAllForgeVersionsLoadedAsync();

            HashSet<string> compatible;
            if (_allForgeVersions is { Count: > 0 })
            {
                // MC-версия поддерживается Forge, если в maven есть версия вида "{mc}-{forge}"
                compatible = new HashSet<string>(
                    _allForgeVersions
                        .Select(v =>
                        {
                            var idx = v.IndexOf('-');
                            return idx > 0 ? v.Substring(0, idx) : null!;
                        })
                        .Where(p => !string.IsNullOrEmpty(p))!);
            }
            else
            {
                compatible = FallbackForgeCompatible;
            }

            ForgeVersions = VanillaVersions
                .Where(v => v.Type == VersionType.Release && compatible.Contains(v.Id))
                .Select(v => new GameVersion
                {
                    Id = $"{v.Id}-forge",
                    DisplayName = v.Id,
                    Type = VersionType.Forge
                })
                .ToList();
        }

        private void LoadDefaultsFallback()
        {
            var defaults = new List<GameVersion>
            {
                new() { Id = "1.20.1", DisplayName = "1.20.1", Type = VersionType.Release },
                new() { Id = "1.19.4", DisplayName = "1.19.4", Type = VersionType.Release },
                new() { Id = "1.18.2", DisplayName = "1.18.2", Type = VersionType.Release },
                new() { Id = "1.16.5", DisplayName = "1.16.5", Type = VersionType.Release },
                new() { Id = "1.12.2", DisplayName = "1.12.2", Type = VersionType.Release },
                new() { Id = "1.8.9",  DisplayName = "1.8.9",  Type = VersionType.Release },
            };
            VanillaVersions = defaults;
            AvailableVersions.Clear();
            foreach (var v in defaults) AvailableVersions.Add(v);
            IsLoaded = true;
        }

        // ===== Проверка установки =====

        /// <summary>Проверяет установлена ли конкретная версия игры</summary>
        public (bool installed, long sizeBytes) CheckInstallation(GameVersion version)
        {
            try
            {
                var versionsDir = PathService.VersionsDirectory;
                if (!Directory.Exists(versionsDir)) return (false, 0);

                // Для Vanilla — ищем папку с точным именем и файл .jar
                if (version.Type == VersionType.Release ||
                    version.Type == VersionType.Snapshot ||
                    version.Type == VersionType.OldBeta ||
                    version.Type == VersionType.OldAlpha)
                {
                    var folder = Path.Combine(versionsDir, version.Id);
                    var jar = Path.Combine(folder, $"{version.Id}.jar");
                    var json = Path.Combine(folder, $"{version.Id}.json");
                    if (File.Exists(jar) && File.Exists(json))
                    {
                        return (true, GetFolderSize(folder));
                    }
                    return (false, 0);
                }

                // Извлекаем чистую MC-версию из DisplayName
                // Примеры:
                //   "1.21.11" -> "1.21.11"
                //   "1.21.11 Fabric" -> "1.21.11"
                //   "1.21.11 Fabric 0.16.14" -> "1.21.11"
                //   "1.20.1 Forge 47.2.0" -> "1.20.1"
                string mcVer = version.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

                // Для Fabric — ищем папки типа "fabric-loader-*-{mcVersion}"
                if (version.Type == VersionType.Fabric)
                {
                    var dirs = Directory.GetDirectories(versionsDir)
                        .Where(d =>
                        {
                            var name = Path.GetFileName(d);
                            return name.StartsWith("fabric-loader-") && name.EndsWith($"-{mcVer}");
                        })
                        .ToList();

                    if (dirs.Count > 0)
                    {
                        long totalSize = 0;
                        foreach (var d in dirs) totalSize += GetFolderSize(d);
                        return (true, totalSize);
                    }
                    return (false, 0);
                }

                // Для Forge — ищем папки типа "{mcVersion}-forge-*"
                if (version.Type == VersionType.Forge)
                {
                    var dirs = Directory.GetDirectories(versionsDir)
                        .Where(d =>
                        {
                            var name = Path.GetFileName(d);
                            return name.StartsWith($"{mcVer}-forge") || name.Contains($"forge-{mcVer}");
                        })
                        .ToList();

                    if (dirs.Count > 0)
                    {
                        long totalSize = 0;
                        foreach (var d in dirs) totalSize += GetFolderSize(d);
                        return (true, totalSize);
                    }
                    return (false, 0);
                }

                return (false, 0);
            }
            catch
            {
                return (false, 0);
            }
        }

        /// <summary>Обновляет статус установки для одной версии</summary>
        public void RefreshInstallStatus(GameVersion version)
        {
            var (installed, size) = CheckInstallation(version);
            version.IsInstalled = installed;
            version.InstalledSizeBytes = size;
        }

        /// <summary>Обновляет статус для всех версий (может занимать время)</summary>
        public void RefreshAllInstallStatuses()
        {
            foreach (var v in VanillaVersions) RefreshInstallStatus(v);
            foreach (var v in FabricVersions) RefreshInstallStatus(v);
            foreach (var v in ForgeVersions) RefreshInstallStatus(v);
        }

        private static long GetFolderSize(string folder)
        {
            try
            {
                long size = 0;
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; } catch { }
                }
                return size;
            }
            catch { return 0; }
        }

        public bool IsInstalled(string versionId)
        {
            var path = Path.Combine(PathService.VersionsDirectory, versionId);
            return Directory.Exists(path);
        }

        // ===== FABRIC LOADER VERSIONS =====
        public async Task<List<string>> GetFabricLoaderVersionsAsync(string mcVersion)
        {
            if (_fabricLoaderCache.TryGetValue(mcVersion, out var cached))
                return cached;

            var result = new List<string>();
            try
            {
                var json = await _http.GetStringAsync($"{FabricLoaderApi}/{mcVersion}");
                using var doc = JsonDocument.Parse(json);

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("loader", out var loader) &&
                        loader.TryGetProperty("version", out var ver))
                    {
                        var version = ver.GetString();
                        if (!string.IsNullOrEmpty(version))
                            result.Add(version);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fabric] Loader load failed: {ex.Message}");
            }

            _fabricLoaderCache[mcVersion] = result;
            return result;
        }

        // ===== FORGE VERSIONS =====
        private async Task EnsureAllForgeVersionsLoadedAsync()
        {
            if (_allForgeVersions != null) return;

            try
            {
                var xml = await _http.GetStringAsync(ForgeMavenMeta);
                var doc = XDocument.Parse(xml);

                _allForgeVersions = doc.Descendants("version")
                    .Select(x => x.Value)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Forge] Meta load failed: {ex.Message}");
                _allForgeVersions = new List<string>();
            }
        }

        public async Task<List<string>> GetForgeVersionsAsync(string mcVersion)
        {
            if (_forgeVersionCache.TryGetValue(mcVersion, out var cached))
                return cached;

            await EnsureAllForgeVersionsLoadedAsync();

            var prefix = mcVersion + "-";
            var result = _allForgeVersions!
                .Where(v => v.StartsWith(prefix))
                .Select(v => v.Substring(prefix.Length))
                .Reverse()
                .ToList();

            _forgeVersionCache[mcVersion] = result;
            return result;
        }
    }
}