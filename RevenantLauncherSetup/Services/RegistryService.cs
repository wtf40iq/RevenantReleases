using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace RevenantLauncherSetup.Services
{
    public static class RegistryService
    {
        private const string UninstallKeyName = "RevenantLauncher";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
        private const string DisplayName = "Revenant Launcher";
        private const string Publisher = "lotesnowhallen";
        private static string Version =>
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.2.1";
        private const string HelpLink = "https://t.me/lotesnowhallen";

        /// <summary>Регистрирует лаунчер в списке установленных программ Windows</summary>
        public static void RegisterInstallation(string installPath, string uninstallerPath, string? iconPath = null)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath + UninstallKeyName);
                if (key == null) return;

                key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", Version, RegistryValueKind.String);
                key.SetValue("Publisher", Publisher, RegistryValueKind.String);
                key.SetValue("InstallLocation", installPath, RegistryValueKind.String);
                key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("URLInfoAbout", HelpLink, RegistryValueKind.String);
                key.SetValue("HelpLink", HelpLink, RegistryValueKind.String);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    key.SetValue("DisplayIcon", iconPath, RegistryValueKind.String);

                // Расчёт размера в KB
                try
                {
                    long size = GetFolderSizeKb(installPath);
                    key.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registry] Register failed: {ex.Message}");
            }
        }

        /// <summary>Удаляет запись из реестра при деинсталляции</summary>
        public static void UnregisterInstallation()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath + UninstallKeyName, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registry] Unregister failed: {ex.Message}");
            }
        }

        /// <summary>Получает путь установки из реестра (для uninstall режима)</summary>
        public static string? GetInstallPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath + UninstallKeyName);
                return key?.GetValue("InstallLocation") as string;
            }
            catch
            {
                return null;
            }
        }

        private static long GetFolderSizeKb(string path)
        {
            long size = 0;
            if (!Directory.Exists(path)) return 0;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size / 1024;
        }
    }
}