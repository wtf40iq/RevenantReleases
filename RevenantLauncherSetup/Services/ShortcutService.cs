using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RevenantLauncherSetup.Services
{
    public static class ShortcutService
    {
        /// <summary>Создать ярлык на рабочем столе</summary>
        public static void CreateDesktopShortcut(string targetExePath, string shortcutName, string? iconPath = null)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, $"{shortcutName}.lnk");
            CreateShortcut(shortcutPath, targetExePath, iconPath);
        }

        /// <summary>Создать ярлык в меню Пуск</summary>
        public static void CreateStartMenuShortcut(string targetExePath, string shortcutName, string? iconPath = null)
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var folder = Path.Combine(startMenu, shortcutName);
            Directory.CreateDirectory(folder);
            var shortcutPath = Path.Combine(folder, $"{shortcutName}.lnk");
            CreateShortcut(shortcutPath, targetExePath, iconPath);
        }

        /// <summary>Удалить ярлык с рабочего стола</summary>
        public static void RemoveDesktopShortcut(string shortcutName)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var shortcutPath = Path.Combine(desktop, $"{shortcutName}.lnk");
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
            }
            catch { }
        }

        /// <summary>Удалить папку в меню Пуск</summary>
        public static void RemoveStartMenuShortcut(string shortcutName)
        {
            try
            {
                var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var folder = Path.Combine(startMenu, shortcutName);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch { }
        }

        /// <summary>Создать .lnk файл через COM WScript.Shell</summary>
        private static void CreateShortcut(string shortcutPath, string targetPath, string? iconPath = null)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
                shortcut.Description = "Revenant Launcher";
                shortcut.WindowStyle = 1;

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    shortcut.IconLocation = iconPath;

                shortcut.Save();

                Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shortcut] Failed: {ex.Message}");
            }
        }
    }
}