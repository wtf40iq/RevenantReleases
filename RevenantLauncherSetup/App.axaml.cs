using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RevenantLauncherSetup.Services;
using RevenantLauncherSetup.Views;
using System;
using System.IO;

namespace RevenantLauncherSetup
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var args = Environment.GetCommandLineArgs();
                bool updateMode = HasArgument(args, "--update");
                bool uninstallMode = !updateMode && DetectUninstallMode();
                var updatePath = GetArgumentValue(args, "--install-path");
                var launcherPidText = GetArgumentValue(args, "--launcher-pid");
                var launcherPid = int.TryParse(launcherPidText, out var parsedPid) ? parsedPid : 0;
                desktop.MainWindow = new SetupWindow(uninstallMode, updateMode, updatePath, launcherPid);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (var arg in args)
                if (arg.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string? GetArgumentValue(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1].Trim().Trim('"');
            }
            return null;
        }

        /// <summary>Определяет режим удаления по аргументам ИЛИ по имени exe</summary>
        private static bool DetectUninstallMode()
        {
            try
            {
                // 1. Проверка аргументов командной строки
                var args = Environment.GetCommandLineArgs();
                foreach (var arg in args)
                {
                    var lower = arg.ToLower();
                    if (lower.Contains("--uninstall") || lower.Contains("/uninstall") || lower == "-u")
                        return true;
                }

                // 2. Проверка по имени exe — если файл называется "unins*",
                // значит это копия установщика для удаления
                var exePath = Environment.ProcessPath ?? "";
                var exeName = Path.GetFileNameWithoutExtension(exePath).ToLower();

                if (exeName.StartsWith("unins"))
                    return true;

                // 3. Проверка расположения — если exe лежит в папке установки лаунчера
                var installDir = Path.GetDirectoryName(exePath) ?? "";
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var expectedInstallDir = Path.Combine(localAppData, "Programs", "RevenantLauncher");

                if (installDir.Equals(expectedInstallDir, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 4. Проверка по пути установки из реестра (если установка была в другую папку)
                var registeredPath = RegistryService.GetInstallPath();
                if (!string.IsNullOrEmpty(registeredPath) &&
                    installDir.Equals(registeredPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}