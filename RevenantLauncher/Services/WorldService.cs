using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class WorldService
    {
        private static readonly Lazy<WorldService> _instance = new(() => new WorldService());
        public static WorldService Instance => _instance.Value;

        /// <summary>Игровая папка (там же saves/, screenshots/)</summary>
        public string GameDirectory =>
            string.IsNullOrEmpty(ConfigService.Instance.Data.Settings.GameDirectory)
                ? PathService.MinecraftDirectory
                : ConfigService.Instance.Data.Settings.GameDirectory;

        public string SavesDirectory => Path.Combine(GameDirectory, "saves");
        public string ScreenshotsDirectory => Path.Combine(GameDirectory, "screenshots");
        public string BackupsDirectory => Path.Combine(GameDirectory, "backups");

        // ===== Миры =====

        public async Task<List<WorldInfo>> LoadWorldsAsync()
        {
            var result = new List<WorldInfo>();
            var saves = SavesDirectory;
            if (!Directory.Exists(saves)) return result;

            var backupCounts = CountBackupsByWorld();

            foreach (var dir in Directory.GetDirectories(saves))
            {
                var folderName = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, "level.dat"))) continue;

                var info = new WorldInfo
                {
                    FolderName = folderName,
                    Name = ReadLevelName(dir) ?? folderName,
                    Path = dir,
                    LastPlayed = Directory.GetLastWriteTime(dir),
                    BackupCount = backupCounts.TryGetValue(folderName, out var c) ? c : 0
                };

                try { info.SizeBytes = await Task.Run(() => GetFolderSize(dir)); } catch { }

                var iconPath = Path.Combine(dir, "icon.png");
                if (File.Exists(iconPath))
                {
                    try
                    {
                        using var fs = File.OpenRead(iconPath);
                        info.Icon = Bitmap.DecodeToWidth(fs, 96, BitmapInterpolationMode.HighQuality);
                    }
                    catch { }
                }

                result.Add(info);
            }

            return result.OrderByDescending(w => w.LastPlayed).ToList();
        }

        public async Task DeleteWorldAsync(WorldInfo world)
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(world.Path))
                    Directory.Delete(world.Path, true);
            });
        }

        public void OpenWorldFolder(WorldInfo world) => OpenFolder(world.Path);

        // ===== Бэкапы =====

        public async Task<List<BackupInfo>> LoadBackupsAsync()
        {
            var result = new List<BackupInfo>();
            var backups = BackupsDirectory;
            if (!Directory.Exists(backups)) return result;

            foreach (var file in Directory.GetFiles(backups, "*.zip"))
            {
                var fi = new FileInfo(file);
                // Пустые/битые бэкапы (остатки прерванных операций) удаляем и не показываем
                if (fi.Length == 0)
                {
                    try { fi.Delete(); } catch { }
                    continue;
                }
                try
                {
                    using var probe = ZipFile.OpenRead(file);
                    if (probe.Entries.Count == 0)
                    {
                        try { File.Delete(file); } catch { }
                        continue;
                    }
                }
                catch
                {
                    try { File.Delete(file); } catch { }
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);

                string worldName = name;
                string stamp = "";
                var sep = name.LastIndexOf("__", StringComparison.Ordinal);
                if (sep > 0)
                {
                    worldName = name.Substring(0, sep);
                    stamp = name.Substring(sep + 2);
                }

                result.Add(new BackupInfo
                {
                    FilePath = file,
                    WorldName = worldName,
                    Stamp = stamp,
                    SizeBytes = fi.Length,
                    Created = fi.CreationTime
                });
            }

            return result.OrderByDescending(b => b.Created).ToList();
        }

        /// <summary>Создаёт zip-бэкап мира: backups/{мир}__{дата}.zip.
        /// Пишет во временный файл и переименовывает только при успехе —
        /// при ошибке (например, мир запущен) пустой бэкап не остаётся.</summary>
        public async Task<BackupInfo> CreateBackupAsync(WorldInfo world)
        {
            Directory.CreateDirectory(BackupsDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(BackupsDirectory, $"{world.FolderName}__{stamp}.zip");
            var temp = dest + ".part";

            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                    ZipFile.CreateFromDirectory(world.Path, temp, CompressionLevel.Optimal, false);

                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                }
                catch
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                    throw;
                }
            });

            var fi = new FileInfo(dest);
            return new BackupInfo
            {
                FilePath = dest,
                WorldName = world.FolderName,
                Stamp = stamp,
                SizeBytes = fi.Length,
                Created = fi.CreationTime
            };
        }

        /// <summary>Восстанавливает мир из бэкапа (перезаписывает папку мира)</summary>
        public async Task RestoreBackupAsync(BackupInfo backup)
        {
            // Имя мира приходит из имени файла бэкапа — защищаемся от пути-траверсала,
            // чтобы ".." или разделители не увели распаковку за пределы saves/
            var worldName = backup.WorldName;
            if (string.IsNullOrWhiteSpace(worldName) ||
                worldName.Contains("..", StringComparison.Ordinal) ||
                worldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("Некорректное имя мира в бэкапе: " + worldName);
            }

            var target = Path.Combine(SavesDirectory, worldName);

            await Task.Run(() =>
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, true);

                ZipFile.ExtractToDirectory(backup.FilePath, target);
            });
        }

        public async Task DeleteBackupAsync(BackupInfo backup)
        {
            await Task.Run(() =>
            {
                if (File.Exists(backup.FilePath))
                    File.Delete(backup.FilePath);
            });
        }

        public void OpenBackupsFolder() => OpenFolder(BackupsDirectory);

        public void OpenSavesFolder() => OpenFolder(SavesDirectory);

        // ===== Скриншоты =====

        public async Task<List<ScreenshotInfo>> LoadScreenshotsAsync()
        {
            var result = new List<ScreenshotInfo>();
            var shots = ScreenshotsDirectory;
            if (!Directory.Exists(shots)) return result;

            var files = Directory.GetFiles(shots, "*.png")
                .OrderByDescending(File.GetLastWriteTime)
                .Take(200)
                .ToList();

            foreach (var file in files)
            {
                var info = new ScreenshotInfo
                {
                    FilePath = file,
                    Date = ParseScreenshotDate(file)
                };

                try
                {
                    using var fs = File.OpenRead(file);
                    info.Thumb = Bitmap.DecodeToWidth(fs, 480, BitmapInterpolationMode.HighQuality);
                }
                catch { continue; }

                result.Add(info);
            }

            return result;
        }

        public async Task DeleteScreenshotAsync(ScreenshotInfo shot)
        {
            await Task.Run(() =>
            {
                if (File.Exists(shot.FilePath))
                    File.Delete(shot.FilePath);
            });
        }

        public void OpenScreenshotsFolder() => OpenFolder(ScreenshotsDirectory);

        public void OpenFile(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        // ===== Helpers =====

        private static DateTime ParseScreenshotDate(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                return DateTime.ParseExact(name, "yyyy-MM-dd_HH.mm.ss",
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return File.GetLastWriteTime(file);
            }
        }

        private Dictionary<string, int> CountBackupsByWorld()
        {
            var map = new Dictionary<string, int>();
            if (!Directory.Exists(BackupsDirectory)) return map;

            foreach (var file in Directory.GetFiles(BackupsDirectory, "*.zip"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var sep = name.LastIndexOf("__", StringComparison.Ordinal);
                var world = sep > 0 ? name.Substring(0, sep) : name;
                map.TryGetValue(world, out var c);
                map[world] = c + 1;
            }
            return map;
        }

        private static long GetFolderSize(string folder)
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        // ===== Чтение LevelName из level.dat (NBT) =====

        /// <summary>Мини-парсер NBT: ищет строковый тег LevelName
        /// (internal — доступен юнит-тестам)</summary>
        internal static string? ReadLevelName(string worldDir)
        {
            try
            {
                var path = Path.Combine(worldDir, "level.dat");
                using var fs = File.OpenRead(path);
                using var gz = new GZipStream(fs, CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                var data = ms.ToArray();

                int pos = 0;
                return FindStringTag(data, ref pos, "LevelName", 0);
            }
            catch
            {
                return null;
            }
        }

        private static string? FindStringTag(byte[] d, ref int pos, string target, int depth)
        {
            if (depth > 6 || pos >= d.Length) return null;

            var tagType = d[pos++];
            if (tagType == 0) return null;

            // имя тега
            var name = ReadTagName(d, ref pos);

            switch (tagType)
            {
                case 1: pos += 1; return null;
                case 2: pos += 2; return null;
                case 3: pos += 4; return null;
                case 4: pos += 8; return null;
                case 5: pos += 4; return null;
                case 6: pos += 8; return null;
                case 7:
                    {
                        var len = ReadInt(d, ref pos);
                        pos += len;
                        return null;
                    }
                case 8:
                    {
                        var len = ReadInt(d, ref pos);
                        var value = Encoding.UTF8.GetString(d, pos, len);
                        pos += len;
                        return name == target ? value : null;
                    }
                case 9:
                    {
                        var elemType = d[pos++];
                        var count = ReadInt(d, ref pos);
                        for (int i = 0; i < count; i++)
                        {
                            var r = SkipOrFind(d, ref pos, elemType, target, depth + 1);
                            if (r != null) return r;
                        }
                        return null;
                    }
                case 10:
                    {
                        while (pos < d.Length)
                        {
                            var child = d[pos];
                            if (child == 0) { pos++; break; }
                            var r = FindStringTag(d, ref pos, target, depth + 1);
                            if (r != null) return r;
                        }
                        return null;
                    }
                case 11:
                    {
                        var count = ReadInt(d, ref pos);
                        pos += count * 4;
                        return null;
                    }
                case 12:
                    {
                        var count = ReadInt(d, ref pos);
                        pos += count * 8;
                        return null;
                    }
                default:
                    return null;
            }
        }

        private static string? SkipOrFind(byte[] d, ref int pos, byte tagType, string target, int depth)
        {
            // элементы списков не имеют имён
            switch (tagType)
            {
                case 8:
                    {
                        var len = ReadInt(d, ref pos);
                        pos += len;
                        return null;
                    }
                case 10:
                    {
                        while (pos < d.Length)
                        {
                            var child = d[pos];
                            if (child == 0) { pos++; break; }
                            var r = FindStringTag(d, ref pos, target, depth + 1);
                            if (r != null) return r;
                        }
                        return null;
                    }
                default:
                    return FindUnnamed(d, ref pos, tagType, target, depth);
            }
        }

        private static string? FindUnnamed(byte[] d, ref int pos, byte tagType, string target, int depth)
        {
            switch (tagType)
            {
                case 1: pos += 1; return null;
                case 2: pos += 2; return null;
                case 3: pos += 4; return null;
                case 4: pos += 8; return null;
                case 5: pos += 4; return null;
                case 6: pos += 8; return null;
                case 7: pos += ReadInt(d, ref pos); return null;
                case 8:
                    {
                        var len = ReadInt(d, ref pos);
                        pos += len;
                        return null;
                    }
                case 9:
                    {
                        var elemType = d[pos++];
                        var count = ReadInt(d, ref pos);
                        for (int i = 0; i < count; i++)
                        {
                            var r = FindUnnamed(d, ref pos, elemType, target, depth + 1);
                            if (r != null) return r;
                        }
                        return null;
                    }
                case 10:
                    {
                        while (pos < d.Length)
                        {
                            var child = d[pos];
                            if (child == 0) { pos++; break; }
                            var r = FindStringTag(d, ref pos, target, depth + 1);
                            if (r != null) return r;
                        }
                        return null;
                    }
                case 11: pos += ReadInt(d, ref pos) * 4; return null;
                case 12: pos += ReadInt(d, ref pos) * 8; return null;
                default: return null;
            }
        }

        private static string ReadTagName(byte[] d, ref int pos)
        {
            var len = (d[pos] << 8) | d[pos + 1];
            pos += 2;
            var name = Encoding.UTF8.GetString(d, pos, len);
            pos += len;
            return name;
        }

        private static int ReadInt(byte[] d, ref int pos)
        {
            var v = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
            pos += 4;
            return v;
        }
    }
}
