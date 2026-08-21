using System;
using System.IO;

namespace RevenantLauncher.Services
{
    public static class PathService
    {
        /// <summary>%AppData%\RevenantLauncher</summary>
        public static string RootDirectory { get; }

        /// <summary>Файл конфига launcher.json</summary>
        public static string ConfigFile { get; }

        /// <summary>Файл с зашифрованным refresh-токеном аккаунта лаунчера</summary>
        public static string AuthTokenFile { get; }

        /// <summary>Папка minecraft (.minecraft-стиль)</summary>
        public static string MinecraftDirectory { get; }

        /// <summary>Папка версий</summary>
        public static string VersionsDirectory { get; }

        /// <summary>Общая папка модов (устаревшая, оставлена для совместимости)</summary>
        public static string ModsDirectory { get; }

        /// <summary>Корневая папка для модов по версиям (mods_per_version)</summary>
        public static string ModsPerVersionDirectory { get; }

        /// <summary>Папка ресурспаков</summary>
        public static string ResourcePacksDirectory { get; }

        /// <summary>Папка для логов лаунчера</summary>
        public static string LogsDirectory { get; }

        /// <summary>Папка для кеша иконок модов</summary>
        public static string IconCacheDirectory { get; }

        /// <summary>Папка для кеша данных (списки версий и т.д.)</summary>
        public static string DataCacheDirectory { get; }

        static PathService()
        {
            RootDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RevenantLauncher");

            ConfigFile = Path.Combine(RootDirectory, "launcher.json");
            AuthTokenFile = Path.Combine(RootDirectory, "auth.dat");
            MinecraftDirectory = Path.Combine(RootDirectory, "minecraft");
            VersionsDirectory = Path.Combine(MinecraftDirectory, "versions");
            ModsDirectory = Path.Combine(MinecraftDirectory, "mods");
            ModsPerVersionDirectory = Path.Combine(MinecraftDirectory, "mods_per_version");
            ResourcePacksDirectory = Path.Combine(MinecraftDirectory, "resourcepacks");
            LogsDirectory = Path.Combine(RootDirectory, "logs");
            IconCacheDirectory = Path.Combine(RootDirectory, "icon_cache");
            DataCacheDirectory = Path.Combine(RootDirectory, "data_cache");
        }

        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(MinecraftDirectory);
            Directory.CreateDirectory(VersionsDirectory);
            Directory.CreateDirectory(ModsDirectory);
            Directory.CreateDirectory(ModsPerVersionDirectory);
            Directory.CreateDirectory(ResourcePacksDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(IconCacheDirectory);
            Directory.CreateDirectory(DataCacheDirectory);
        }

        /// <summary>Получить папку модов для конкретной версии MC + loader (например "1.21.11-fabric")</summary>
        public static string GetModsFolderForVersion(string mcVersion, string loader)
        {
            var folderName = SanitizeFolderName($"{mcVersion}-{loader.ToLower()}");
            var path = Path.Combine(ModsPerVersionDirectory, folderName);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Получить папку модов из DisplayName версии ("1.21.11 Fabric 0.16.14" → "1.21.11-fabric")</summary>
        public static string GetModsFolderFromDisplayName(string displayName)
        {
            var (mcVer, loader) = ParseDisplayName(displayName);
            if (string.IsNullOrEmpty(loader))
                loader = "vanilla";
            return GetModsFolderForVersion(mcVer, loader);
        }

        /// <summary>Парсит "1.21.11 Fabric 0.16.14" → ("1.21.11", "fabric")</summary>
        public static (string mcVer, string loader) ParseDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return ("1.20.1", "vanilla");

            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return ("1.20.1", "vanilla");

            var mc = parts[0];
            string loader = "vanilla";
            if (parts.Length >= 2)
            {
                var second = parts[1].ToLower();
                if (second == "fabric" || second == "forge" || second == "quilt" || second == "neoforge")
                    loader = second;
            }
            return (mc, loader);
        }

        private static string SanitizeFolderName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = name;
            foreach (var c in invalid)
                sanitized = sanitized.Replace(c, '_');
            return sanitized;
        }
    }
}