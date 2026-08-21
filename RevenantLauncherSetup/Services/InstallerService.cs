using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RevenantLauncherSetup.Models;

namespace RevenantLauncherSetup.Services
{
    public class InstallerService
    {
        private const string LauncherExeName = "RevenantLauncher.exe";
        private const string UninstallerExeName = "unins000.exe";
        private const string ShortcutName = "Revenant Launcher";

        /// <summary>Полная установка лаунчера</summary>
        public async Task InstallAsync(InstallOptions options, Action<double, string> progress)
        {
            progress(0, "Проверка ресурсов...");
            await Task.Delay(300);

            if (!PayloadExtractor.IsPayloadAvailable())
                throw new InvalidOperationException(
                    "Установочные файлы не найдены. Возможно, установщик повреждён.");

            progress(5, options.IsUpdate ? "Ожидание закрытия лаунчера..." : "Подготовка папки установки...");
            await Task.Delay(200);

            if (options.IsUpdate)
                await WaitForLauncherToCloseAsync(options.InstallPath, options.LauncherProcessId);

            // Создаём папку установки
            Directory.CreateDirectory(options.InstallPath);

            // Удаляем старые файлы ТОЛЬКО если папка пустая или уже содержит установку
            // лаунчера. Иначе можно случайно стереть данные пользователя в посторонней папке.
            if (Directory.Exists(options.InstallPath))
            {
                var existingFiles = Directory.GetFiles(options.InstallPath);
                if (existingFiles.Length > 0)
                {
                    var looksLikeLauncher = existingFiles.Any(f =>
                        Path.GetFileName(f).Equals(LauncherExeName, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(f).Equals(UninstallerExeName, StringComparison.OrdinalIgnoreCase));

                    if (!looksLikeLauncher)
                    {
                        throw new InvalidOperationException(
                            "Выбранная папка не пуста и не похожа на установку Revenant Launcher. " +
                            "Укажи пустую папку или папку с существующей установкой.");
                    }
                }

                try
                {
                    foreach (var file in existingFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                catch { }
            }

            // 1. Распаковка payload.zip
            progress(10, "Распаковка файлов лаунчера...");
            await PayloadExtractor.ExtractAsync(options.InstallPath, (p, msg) =>
            {
                // Распаковка = 10% до 70% общего прогресса
                double totalProgress = 10 + (p * 0.6);
                progress(totalProgress, msg);
            });

            progress(75, "Копирование установщика для удаления...");
            await Task.Delay(200);

            // 2. Копируем сам себя как uninstaller
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            var uninstallerPath = Path.Combine(options.InstallPath, UninstallerExeName);

            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            {
                try
                {
                    File.Copy(currentExe, uninstallerPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Installer] Uninstaller copy failed: {ex.Message}");
                }
            }

            // 3. Создание ярлыков
            progress(85, "Создание ярлыков...");
            await Task.Delay(200);

            var launcherExe = Path.Combine(options.InstallPath, LauncherExeName);
            var iconPath = launcherExe; // Иконка берётся из EXE

            if (options.CreateDesktopShortcut)
            {
                await Task.Run(() =>
                    ShortcutService.CreateDesktopShortcut(launcherExe, ShortcutName, iconPath));
            }

            if (options.CreateStartMenuShortcut)
            {
                await Task.Run(() =>
                    ShortcutService.CreateStartMenuShortcut(launcherExe, ShortcutName, iconPath));
            }

            // 4. Регистрация в реестре
            progress(95, "Регистрация в системе...");
            await Task.Delay(200);

            RegistryService.RegisterInstallation(options.InstallPath, uninstallerPath, iconPath);

            progress(100, "Установка завершена!");
            await Task.Delay(300);
        }

        private static async Task WaitForLauncherToCloseAsync(string installPath, int launcherProcessId)
        {
            if (launcherProcessId > 0)
            {
                try
                {
                    using var process = Process.GetProcessById(launcherProcessId);
                    while (!process.HasExited)
                        await Task.Delay(200);
                }
                catch (ArgumentException)
                {
                    // Процесс уже завершился — продолжаем обновление.
                }
                catch (InvalidOperationException)
                {
                    // Процесс уже завершился — продолжаем обновление.
                }

                await Task.Delay(500);
            }

            var launcherPath = Path.Combine(installPath, LauncherExeName);
            if (!File.Exists(launcherPath)) return;

            // Даём Windows/антивирусу отпустить файл после завершения процесса.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var stream = new FileStream(launcherPath, FileMode.Open, FileAccess.Read,
                        FileShare.None);
                    return;
                }
                catch (IOException)
                {
                    await Task.Delay(250);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(250);
                }
            }

            throw new IOException("Не удалось дождаться закрытия текущего лаунчера.");
        }

        /// <summary>Полное удаление лаунчера</summary>
        public async Task UninstallAsync(bool deleteUserData, Action<double, string> progress)
        {
            progress(0, "Начало удаления...");
            await Task.Delay(300);

            // Получаем путь установки из реестра
            var installPath = RegistryService.GetInstallPath();
            if (string.IsNullOrEmpty(installPath))
            {
                // Пробуем угадать
                installPath = InstallOptions.GetDefaultInstallPath();
            }

            // 1. Удаляем ярлыки
            progress(20, "Удаление ярлыков...");
            await Task.Run(() =>
            {
                ShortcutService.RemoveDesktopShortcut(ShortcutName);
                ShortcutService.RemoveStartMenuShortcut(ShortcutName);
            });
            await Task.Delay(200);

            // 2. Удаляем запись из реестра
            progress(35, "Удаление записи из реестра...");
            RegistryService.UnregisterInstallation();
            await Task.Delay(200);

            // 3. Удаляем данные пользователя (если попросил)
            if (deleteUserData)
            {
                progress(55, "Удаление пользовательских данных...");
                await Task.Run(() =>
                {
                    try
                    {
                        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var userDataPath = Path.Combine(appData, "RevenantLauncher");
                        if (Directory.Exists(userDataPath))
                            Directory.Delete(userDataPath, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Uninstall] User data delete failed: {ex.Message}");
                    }
                });
                await Task.Delay(300);
            }

            // 4. Удаляем файлы программы
            progress(75, "Удаление файлов лаунчера...");
            await Task.Run(() =>
            {
                if (Directory.Exists(installPath))
                {
                    try
                    {
                        // Удаляем всё кроме uninstaller.exe (он сейчас запущен)
                        foreach (var file in Directory.GetFiles(installPath))
                        {
                            var fileName = Path.GetFileName(file);
                            if (!fileName.Equals(UninstallerExeName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(file); } catch { }
                            }
                        }

                        foreach (var dir in Directory.GetDirectories(installPath))
                        {
                            try { Directory.Delete(dir, true); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Uninstall] Files delete failed: {ex.Message}");
                    }
                }
            });
            await Task.Delay(300);

            // 5. Планируем удаление uninstaller.exe и папки после закрытия
            progress(90, "Финализация...");
            await Task.Delay(200);

            try
            {
                ScheduleSelfDelete(installPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Uninstall] Self-delete schedule failed: {ex.Message}");
            }

            progress(100, "Удаление завершено!");
            await Task.Delay(300);
        }

        /// <summary>Планирует удаление uninstaller.exe и его папки через .bat скрипт</summary>
        private void ScheduleSelfDelete(string installPath)
        {
            var batPath = Path.Combine(Path.GetTempPath(), $"RevenantLauncherCleanup_{Guid.NewGuid():N}.bat");
            var uninstallerPath = Path.Combine(installPath, UninstallerExeName);

            var script = $@"@echo off
timeout /t 2 /nobreak > nul
del /f /q ""{uninstallerPath}"" 2>nul
rmdir /s /q ""{installPath}"" 2>nul
del /f /q ""{batPath}""
";

            File.WriteAllText(batPath, script);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
        }
    }
}