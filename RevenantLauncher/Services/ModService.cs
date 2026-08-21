using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class ModService
    {
        private static readonly Lazy<ModService> _instance = new(() => new ModService());
        public static ModService Instance => _instance.Value;

        public ObservableCollection<ModInfo> Mods { get; } = new();

        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private string? _currentVersionDisplayName;

        private static string InstalledMapFile =>
            Path.Combine(PathService.MinecraftDirectory, "installed_mods.json");

        private Dictionary<string, string> _installedMap = new();

        private ModService()
        {
            LoadInstalledMap();
        }

        public void SetCurrentVersion(string versionDisplayName)
        {
            _currentVersionDisplayName = versionDisplayName;
        }

        /// <summary>Получить папку модов для текущей версии</summary>
        public string GetVersionModsFolder()
        {
            if (string.IsNullOrEmpty(_currentVersionDisplayName))
                return PathService.ModsDirectory;

            return PathService.GetModsFolderFromDisplayName(_currentVersionDisplayName);
        }

        /// <summary>Текущая версия (DisplayName), для которой работают моды</summary>
        public string? CurrentVersionDisplayName => _currentVersionDisplayName;

        /// <summary>Ключи всех папок модов по версиям (имена подпапок mods_per_version)</summary>
        public List<string> GetModFolderKeys()
        {
            var result = new List<string>();
            var root = PathService.ModsPerVersionDirectory;
            if (!Directory.Exists(root)) return result;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                result.Add(name);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Ключ папки для DisplayName версии ("1.21.8 Forge" -> "1.21.8-forge")</summary>
        public static string FolderKeyFromDisplayName(string displayName)
        {
            var (mc, loader) = PathService.ParseDisplayName(displayName);
            return $"{mc}-{loader}";
        }

        /// <summary>"1.21.11-fabric" -> "1.21.11 Fabric"</summary>
        public static string PrettifyFolderKey(string key)
        {
            var idx = key.LastIndexOf('-');
            if (idx <= 0 || idx == key.Length - 1) return key;
            var mc = key.Substring(0, idx);
            var loader = key.Substring(idx + 1);
            return $"{mc} {char.ToUpper(loader[0])}{loader.Substring(1)}";
        }

        /// <summary>Сколько модов в папке версии (лёгкий подсчёт файлов)</summary>
        public int CountModsInFolder(string folderKey)
        {
            var folder = Path.Combine(PathService.ModsPerVersionDirectory, folderKey);
            if (!Directory.Exists(folder)) return 0;
            return Directory.GetFiles(folder, "*.jar").Length
                 + Directory.GetFiles(folder, "*.jar.disabled").Length;
        }

        /// <summary>Загружает моды строго из указанной папки версии (без фолбэка на общую)</summary>
        public async Task<List<ModInfo>> LoadFolderAsync(string folderKey)
        {
            var folder = Path.Combine(PathService.ModsPerVersionDirectory, folderKey);
            var files = new List<string>();

            if (Directory.Exists(folder))
            {
                files.AddRange(
                    Directory.GetFiles(folder, "*.jar")
                        .Concat(Directory.GetFiles(folder, "*.jar.disabled")));
            }

            files = files.OrderBy(f => Path.GetFileName(f)).ToList();

            var infos = new List<ModInfo>();
            foreach (var f in files)
            {
                try { infos.Add(await ModReaderService.ReadAsync(f)); } catch { }
            }

            return infos;
        }

        /// <summary>Получить общую папку модов (старую)</summary>
        public string GetCommonModsFolder()
        {
            return PathService.ModsDirectory;
        }

        public async Task LoadAsync(string? versionDisplayName = null)
        {
            if (!await _loadLock.WaitAsync(0))
            {
                Console.WriteLine("[ModService] Load already in progress, skipping");
                return;
            }

            try
            {
                var version = versionDisplayName ?? _currentVersionDisplayName;

                // Папка модов для текущей версии
                var versionFolder = !string.IsNullOrEmpty(version)
                    ? PathService.GetModsFolderFromDisplayName(version)
                    : PathService.ModsDirectory;

                // Общая папка модов (старая)
                var commonFolder = PathService.ModsDirectory;

                // Миграция: если в общей папке есть моды, а в версионной нет — предложить перенести
                if (!string.IsNullOrEmpty(version) && versionFolder != commonFolder)
                {
                    MigrateCommonModsIfNeeded(commonFolder, versionFolder);
                }

                // Собираем моды из версионной папки
                var allFiles = new List<string>();

                if (Directory.Exists(versionFolder))
                {
                    allFiles.AddRange(
                        Directory.GetFiles(versionFolder, "*.jar")
                            .Concat(Directory.GetFiles(versionFolder, "*.jar.disabled")));
                }

                allFiles = allFiles.OrderBy(f => Path.GetFileName(f)).ToList();

                var readTasks = allFiles.Select(f => ModReaderService.ReadAsync(f)).ToArray();
                var infos = await Task.WhenAll(readTasks);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Mods.Clear();
                    foreach (var info in infos)
                        Mods.Add(info);
                });

                Console.WriteLine($"[ModService] Loaded {infos.Length} mods");
            }
            finally
            {
                _loadLock.Release();
            }
        }

        /// <summary>Мигрирует моды из общей папки в папку версии (один раз, помечается маркером)</summary>
        private void MigrateCommonModsIfNeeded(string commonFolder, string versionFolder)
        {
            try
            {
                if (!Directory.Exists(commonFolder)) return;

                Directory.CreateDirectory(versionFolder);

                // Маркер: миграция уже выполнялась — повторно не копируем,
                // иначе удалённые моды будут возвращаться из общей папки
                var marker = Path.Combine(versionFolder, ".revenant_migrated");
                if (File.Exists(marker)) return;

                // Если в версионной папке уже есть файлы — не мигрируем
                var existingInVersion = Directory.GetFiles(versionFolder, "*.jar")
                    .Concat(Directory.GetFiles(versionFolder, "*.jar.disabled")).ToArray();

                if (existingInVersion.Length > 0)
                {
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                    return;
                }

                // Копируем (не перемещаем — чтобы не сломать другие версии)
                var commonFiles = Directory.GetFiles(commonFolder, "*.jar")
                    .Concat(Directory.GetFiles(commonFolder, "*.jar.disabled")).ToArray();

                if (commonFiles.Length > 0)
                {
                    Console.WriteLine($"[ModService] Migrating {commonFiles.Length} mods from common to {Path.GetFileName(versionFolder)}");

                    foreach (var file in commonFiles)
                    {
                        var destPath = Path.Combine(versionFolder, Path.GetFileName(file));
                        if (!File.Exists(destPath))
                        {
                            try { File.Copy(file, destPath); } catch { }
                        }
                    }
                }

                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModService] Migration failed: {ex.Message}");
            }
        }

        public async Task<ModInfo> AddModAsync(string sourceFilePath, string? versionDisplayName = null, string? modrinthProjectId = null)
        {
            var folder = GetTargetFolder(versionDisplayName);
            Directory.CreateDirectory(folder);

            var destFileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(folder, destFileName);

            int counter = 1;
            while (File.Exists(destPath))
            {
                var name = Path.GetFileNameWithoutExtension(destFileName);
                var ext = Path.GetExtension(destFileName);
                destPath = Path.Combine(folder, $"{name}_{counter}{ext}");
                counter++;
            }

            await Task.Run(() => File.Copy(sourceFilePath, destPath, false));

            if (!string.IsNullOrEmpty(modrinthProjectId))
                RegisterInstalled(Path.GetFileName(destPath), modrinthProjectId);

            var info = await ModReaderService.ReadAsync(destPath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Mods.Any(m => m.FilePath == info.FilePath))
                    Mods.Add(info);
            });
            return info;
        }

        public async Task<ModInfo> AddFromStreamAsync(Stream stream, string fileName, string? versionDisplayName = null, string? modrinthProjectId = null)
        {
            var folder = GetTargetFolder(versionDisplayName);
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

            var info = await ModReaderService.ReadAsync(destPath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Mods.Any(m => m.FilePath == info.FilePath))
                    Mods.Add(info);
            });
            return info;
        }

        /// <summary>Папка установки: для указанной версии либо для текущей выбранной</summary>
        private string GetTargetFolder(string? versionDisplayName)
        {
            return string.IsNullOrEmpty(versionDisplayName)
                ? GetVersionModsFolder()
                : PathService.GetModsFolderFromDisplayName(versionDisplayName);
        }

        public async Task ToggleModAsync(ModInfo mod)
        {
            await Task.Run(() =>
            {
                var oldPath = mod.FilePath;
                string newPath;

                if (mod.IsEnabled)
                {
                    newPath = oldPath + ".disabled";
                    mod.IsEnabled = false;
                }
                else
                {
                    newPath = oldPath.EndsWith(".disabled")
                        ? oldPath.Substring(0, oldPath.Length - ".disabled".Length)
                        : oldPath;
                    mod.IsEnabled = true;
                }

                if (File.Exists(oldPath) && oldPath != newPath)
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    mod.FilePath = newPath;
                }
            });
        }

        public async Task RemoveModAsync(ModInfo mod)
        {
            var fileName = Path.GetFileName(mod.FilePath).Replace(".disabled", "");
            await Task.Run(() =>
            {
                if (File.Exists(mod.FilePath))
                    File.Delete(mod.FilePath);

                // Удаляем также из общей папки, чтобы мод не "воскрес" при миграции
                try
                {
                    var commonJar = Path.Combine(PathService.ModsDirectory, fileName);
                    if (File.Exists(commonJar)) File.Delete(commonJar);
                    if (File.Exists(commonJar + ".disabled")) File.Delete(commonJar + ".disabled");
                }
                catch { }
            });
            UnregisterInstalled(fileName);
            await Dispatcher.UIThread.InvokeAsync(() => Mods.Remove(mod));
        }

        public async Task<bool> RemoveByProjectIdAsync(string projectId)
        {
            // Находим файл по project_id
            var fileName = FindFileByProjectId(projectId);

            // Если файл не найден в маппинге — просто снимаем регистрацию
            if (string.IsNullOrEmpty(fileName))
            {
                CleanupMapping(projectId);
                return true;
            }

            // Ищем мод в загруженном списке
            var mod = Mods.FirstOrDefault(m =>
                Path.GetFileName(m.FilePath).Replace(".disabled", "")
                    .Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (mod != null)
            {
                await RemoveModAsync(mod);
                return true;
            }

            // Мод не в списке — удаляем файл вручную из всех возможных папок
            var deleted = false;
            var foldersToCheck = new List<string> { GetVersionModsFolder(), GetCommonModsFolder() };

            foreach (var folder in foldersToCheck.Distinct())
            {
                var fullPath = Path.Combine(folder, fileName);
                var disabledPath = fullPath + ".disabled";

                try
                {
                    if (File.Exists(fullPath)) { File.Delete(fullPath); deleted = true; }
                    if (File.Exists(disabledPath)) { File.Delete(disabledPath); deleted = true; }
                }
                catch { }
            }

            UnregisterInstalled(fileName);
            return deleted;
        }

        public void OpenModsFolder(string? versionDisplayName = null)
        {
            try
            {
                var folder = GetVersionModsFolder();
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch { }
        }

        // ===== installed_mods.json =====

        /// <summary>Проверить установлен ли мод — по файлу НА ДИСКЕ, не только по маппингу</summary>
        public bool IsProjectInstalled(string projectId, string? slug = null)
        {
            if (!string.IsNullOrEmpty(projectId))
            {
                var fileName = FindFileByProjectId(projectId);
                if (!string.IsNullOrEmpty(fileName) && FileExistsAnywhere(fileName))
                    return true;
            }

            // Запасной путь: ищем файл по slug проекта (на случай установки без регистрации).
            // Это защищает от повторного скачивания уже установленного мода.
            if (!string.IsNullOrEmpty(slug))
            {
                foreach (var folder in new[] { GetVersionModsFolder(), GetCommonModsFolder() }.Distinct())
                {
                    if (!Directory.Exists(folder)) continue;

                    var files = Directory.GetFiles(folder, "*.jar")
                        .Concat(Directory.GetFiles(folder, "*.jar.disabled"));

                    foreach (var file in files)
                    {
                        var name = Path.GetFileName(file).Replace(".disabled", "");
                        if (MatchesSlug(name, slug))
                        {
                            // Регистрируем найденный файл, чтобы дальше работать по маппингу
                            if (!string.IsNullOrEmpty(projectId))
                                RegisterInstalled(name, projectId);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Совпадает ли имя файла со slug (имена вида "sodium-fabric-0.6.0.jar")</summary>
        private static bool MatchesSlug(string fileName, string slug)
        {
            var baseName = fileName
                .Replace(".jar", "", StringComparison.OrdinalIgnoreCase);

            return baseName.Equals(slug, StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith(slug + "-", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith(slug + "_", StringComparison.OrdinalIgnoreCase);
        }

        public string? FindFileByProjectId(string projectId)
        {
            var entry = _installedMap.FirstOrDefault(kvp =>
                kvp.Value.Equals(projectId, StringComparison.OrdinalIgnoreCase));

            return entry.Key;
        }

        /// <summary>Project id, зарегистрированный для файла мода</summary>
        public string? GetProjectIdForFile(string fileName) =>
            _installedMap.TryGetValue(fileName, out var projectId) ? projectId : null;

        public void RegisterProjectForFile(string fileName, string projectId) =>
            RegisterInstalled(fileName, projectId);

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
                Console.WriteLine($"[ModService] SHA1 failed for {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Заменяет файл мода на новую версию (удаляет старый, сохраняет состояние вкл/выкл)</summary>
        public async Task<ModInfo?> ReplaceModAsync(ModInfo old, Stream stream, string newFileName, string? projectId)
        {
            var folder = Path.GetDirectoryName(old.FilePath);
            if (string.IsNullOrEmpty(folder)) return null;

            var wasEnabled = old.IsEnabled;
            var oldFileName = Path.GetFileName(old.FilePath).Replace(".disabled", "");

            string? newFilePath = null;
            await Task.Run(() =>
            {
                if (File.Exists(old.FilePath))
                    File.Delete(old.FilePath);

                var destPath = Path.Combine(folder, newFileName);
                int counter = 1;
                while (File.Exists(destPath) || File.Exists(destPath + ".disabled"))
                {
                    var name = Path.GetFileNameWithoutExtension(newFileName);
                    var ext = Path.GetExtension(newFileName);
                    destPath = Path.Combine(folder, $"{name}_{counter}{ext}");
                    counter++;
                }

                using (var fs = File.Create(destPath))
                    stream.CopyTo(fs);

                if (!wasEnabled)
                {
                    File.Move(destPath, destPath + ".disabled");
                    destPath += ".disabled";
                }

                newFilePath = destPath;
            });

            if (newFilePath == null) return null;

            UnregisterInstalled(oldFileName);
            if (!string.IsNullOrEmpty(projectId))
                RegisterInstalled(Path.GetFileName(newFilePath).Replace(".disabled", ""), projectId);

            var info = await ModReaderService.ReadAsync(newFilePath);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Mods.Remove(old);
                if (!Mods.Any(m => m.FilePath == info.FilePath))
                    Mods.Add(info);
            });
            return info;
        }

        /// <summary>Проверяет существует ли файл мода в любой из папок</summary>
        private bool FileExistsAnywhere(string fileName)
        {
            var foldersToCheck = new List<string> { GetVersionModsFolder(), GetCommonModsFolder() };

            foreach (var folder in foldersToCheck.Distinct())
            {
                if (!Directory.Exists(folder)) continue;
                var fullPath = Path.Combine(folder, fileName);
                var disabledPath = fullPath + ".disabled";
                if (File.Exists(fullPath) || File.Exists(disabledPath))
                    return true;
            }

            // Файл не найден — очищаем маппинг
            CleanupMapping(null, fileName);
            return false;
        }

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

        /// <summary>Удаляет запись из маппинга по projectId или fileName</summary>
        private void CleanupMapping(string? projectId = null, string? fileName = null)
        {
            bool changed = false;

            if (!string.IsNullOrEmpty(projectId))
            {
                var keysToRemove = _installedMap
                    .Where(kvp => kvp.Value.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => kvp.Key).ToList();

                foreach (var key in keysToRemove)
                {
                    _installedMap.Remove(key);
                    changed = true;
                }
            }

            if (!string.IsNullOrEmpty(fileName) && _installedMap.Remove(fileName))
                changed = true;

            if (changed) SaveInstalledMap();
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
                Console.WriteLine($"[ModService] SaveInstalledMap failed: {ex.Message}");
            }
        }
    }
}