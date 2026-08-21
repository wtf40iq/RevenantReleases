using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ProcessBuilder;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class MinecraftService
    {
        private static readonly Lazy<MinecraftService> _instance = new(() => new MinecraftService());
        public static MinecraftService Instance => _instance.Value;

        public MinecraftLauncher Launcher { get; }
        private readonly FabricInstallerService _fabricInstaller = new();

        public event Action<float>? ProgressChanged;
        public event Action<string>? StatusChanged;
        public event Action<Process>? GameStarted;
        public event Action<Process, int>? GameExited;

        /// <summary>Изменился статус «игра запущена»</summary>
        public event Action<bool>? GameRunningChanged;
        /// <summary>Игра аварийно завершилась: код выхода, описание, путь к логу</summary>
        public event Action<int, string, string>? GameCrashed;

        private readonly List<Process> _runningProcesses = new();
        private readonly object _procLock = new();

        /// <summary>Запущена ли сейчас хотя бы одна копия игры</summary>
        public bool IsGameRunning
        {
            get { lock (_procLock) return _runningProcesses.Count > 0; }
        }

        private MinecraftService()
        {
            var path = new MinecraftPath(PathService.MinecraftDirectory);
            Launcher = new MinecraftLauncher(path);

            Launcher.FileProgressChanged += (sender, args) =>
            {
                StatusChanged?.Invoke($"{args.EventType}: {args.Name} ({args.ProgressedTasks}/{args.TotalTasks})");
                if (args.TotalTasks > 0)
                    ProgressChanged?.Invoke((float)args.ProgressedTasks / args.TotalTasks);
            };

            Launcher.ByteProgressChanged += (sender, args) =>
            {
                if (args.TotalBytes > 0)
                    ProgressChanged?.Invoke((float)args.ProgressedBytes / args.TotalBytes);
            };
        }

        public async Task<Process?> LaunchAsync(GameVersion version, AccountModel account)
        {
            var settings = ConfigService.Instance.Data.Settings;

            try
            {
                StatusChanged?.Invoke("Подготовка...");

                string launchVersionId = await PrepareVersionAsync(version);

                MSession session = account.Type == AccountType.Microsoft && !string.IsNullOrEmpty(account.AccessToken)
                    ? new MSession(account.Username, account.AccessToken, account.Uuid)
                    : MSession.CreateOfflineSession(account.Username);

                // Синхронизируем моды из папки версии в основную mods/
                SyncModsForVersion(version);

                var launchOption = new MLaunchOption
                {
                    Session = session,
                    MinimumRamMb = settings.MinRamMb,
                    MaximumRamMb = settings.MaxRamMb,
                    ScreenWidth = settings.GameWindowWidth,
                    ScreenHeight = settings.GameWindowHeight,
                    FullScreen = settings.Fullscreen,
                    JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath) ? null : settings.JavaPath
                };

                StatusChanged?.Invoke("Запуск игры...");
                var process = await Launcher.InstallAndBuildProcessAsync(launchVersionId, launchOption);

                var launchTime = DateTime.Now;
                var (logWriter, logPath) = CreateLaunchLog();

                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    int exitCode;
                    try { exitCode = process.ExitCode; } catch { exitCode = -1; }

                    try { logWriter.Flush(); logWriter.Dispose(); } catch { }

                    lock (_procLock) _runningProcesses.Remove(process);
                    GameRunningChanged?.Invoke(IsGameRunning);

                    if (exitCode != 0)
                    {
                        var details = AnalyzeCrash(launchTime, logPath, exitCode);
                        GameCrashed?.Invoke(exitCode, details, logPath);
                    }

                    GameExited?.Invoke(process, exitCode);
                };

                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) WriteLaunchLog(logWriter, e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) WriteLaunchLog(logWriter, "[ERR] " + e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                lock (_procLock) _runningProcesses.Add(process);
                GameRunningChanged?.Invoke(true);

                GameStarted?.Invoke(process);
                StatusChanged?.Invoke("Игра запущена");
                ProgressChanged?.Invoke(1f);

                return process;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Ошибка: " + ex.Message);
                WriteLauncherLog("[LAUNCH ERROR] " + ex);
                throw;
            }
        }

        /// <summary>Копирует моды из папки версии в основную папку mods/ перед запуском</summary>
        private void SyncModsForVersion(GameVersion version)
        {
            try
            {
                var (mcVer, loaderName) = PathService.ParseDisplayName(version.DisplayName);

                if (version.Type == VersionType.Release || version.Type == VersionType.Snapshot
                    || version.Type == VersionType.OldBeta || version.Type == VersionType.OldAlpha)
                    loaderName = "vanilla";
                else if (version.Type == VersionType.Fabric) loaderName = "fabric";
                else if (version.Type == VersionType.Forge) loaderName = "forge";
                else if (version.Type == VersionType.Quilt) loaderName = "quilt";
                else if (version.Type == VersionType.NeoForge) loaderName = "neoforge";

                var modsSource = PathService.GetModsFolderForVersion(mcVer, loaderName);
                var modsDest = PathService.ModsDirectory;

                SyncModsFolder(modsSource, modsDest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MinecraftService] SyncModsForVersion failed: {ex.Message}");
            }
        }

        /// <summary>Копирует моды из source в dest (обновляет только изменившиеся).
        /// Удаляются ТОЛЬКО файлы, скопированные сюда прошлой синхронизацией (манифест
        /// .revenant_synced) — моды, добавленные пользователем вручную, не трогаются.
        /// (internal — доступен юнит-тестам)</summary>
        internal static void SyncModsFolder(string source, string dest)
        {
            try
            {
                Directory.CreateDirectory(dest);

                // Имена файлов в source
                var sourceFiles = Directory.Exists(source)
                    ? Directory.GetFiles(source).Select(f => Path.GetFileName(f) ?? "")
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>();

                // Манифест: что мы скопировали в прошлый раз
                var manifestPath = Path.Combine(dest, ".revenant_synced");
                var previouslySynced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        foreach (var line in File.ReadAllLines(manifestPath))
                        {
                            var name = line.Trim();
                            if (!string.IsNullOrEmpty(name))
                                previouslySynced.Add(name);
                        }
                    }
                    catch { /* манифест повреждён — просто не удаляем ничего */ }
                }

                // Удаляем только наши прошлые файлы, которых больше нет в source
                foreach (var name in previouslySynced)
                {
                    if (sourceFiles.Contains(name)) continue;
                    var destFile = Path.Combine(dest, name);
                    try
                    {
                        if (File.Exists(destFile)) File.Delete(destFile);
                    }
                    catch { }
                }

                // Копируем/обновляем актуальные файлы
                if (Directory.Exists(source))
                {
                    foreach (var sourceFile in Directory.GetFiles(source))
                    {
                        var name = Path.GetFileName(sourceFile);
                        var destFile = Path.Combine(dest, name ?? "");

                        if (!File.Exists(destFile) ||
                            new FileInfo(sourceFile).Length != new FileInfo(destFile).Length)
                        {
                            try
                            {
                                File.Copy(sourceFile, destFile, overwrite: true);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[MinecraftService] Copy mod failed: {ex.Message}");
                            }
                        }
                    }
                }

                // Обновляем манифест
                try
                {
                    File.WriteAllLines(manifestPath, sourceFiles);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MinecraftService] SyncModsFolder failed: {ex.Message}");
            }
        }

        private async Task<string> PrepareVersionAsync(GameVersion version)
        {
            switch (version.Type)
            {
                case VersionType.Forge:
                    {
                        StatusChanged?.Invoke("Установка Forge...");
                        var forge = new ForgeInstaller(Launcher);
                        var baseVersion = version.Id.Replace("-forge", "");

                        string forgeVersionName;
                        if (!string.IsNullOrEmpty(version.LoaderVersion))
                            forgeVersionName = await forge.Install(baseVersion, version.LoaderVersion);
                        else
                            forgeVersionName = await forge.Install(baseVersion);

                        return forgeVersionName;
                    }

                case VersionType.Fabric:
                    {
                        StatusChanged?.Invoke("Установка Fabric...");
                        var baseVersion = version.Id.Replace("-fabric", "");
                        var fabricVersionId = await _fabricInstaller.InstallAsync(
                            baseVersion,
                            version.LoaderVersion,
                            PathService.VersionsDirectory);
                        return fabricVersionId;
                    }

                default:
                    return version.Id;
            }
        }

        // ===== Логи запусков игры =====

        /// <summary>Максимум файлов лога запусков (старые удаляются)</summary>
        private const int MaxLaunchLogs = 30;

        /// <summary>Создаёт файл лога для нового запуска и чистит старые</summary>
        private (StreamWriter writer, string path) CreateLaunchLog()
        {
            var dir = Path.Combine(PathService.LogsDirectory, "game");
            Directory.CreateDirectory(dir);

            // Лимит количества логов — удаляем самые старые
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("launch-*.log")
                    .OrderBy(f => f.LastWriteTime).ToList();
                while (files.Count >= MaxLaunchLogs)
                {
                    try { files[0].Delete(); } catch { }
                    files.RemoveAt(0);
                }
            }
            catch { }

            var path = Path.Combine(dir, $"launch-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            var writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };
            return (writer, path);
        }

        private void WriteLaunchLog(StreamWriter writer, string line)
        {
            try
            {
                lock (writer)
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            }
            catch { /* лог уже закрыт */ }
        }

        /// <summary>Общий лог лаунчера (ошибки запуска и т.п.), один файл в день</summary>
        private static readonly object _logLock = new();
        private void WriteLauncherLog(string line)
        {
            try
            {
                lock (_logLock)
                {
                    var file = Path.Combine(PathService.LogsDirectory, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Собирает описание краша: код выхода, crash-report Minecraft, ошибки из лога</summary>
        private string AnalyzeCrash(DateTime launchTime, string logPath, int exitCode)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Код выхода: {exitCode} — {DescribeExitCode(exitCode)}");

            // Ищем свежий crash-report Minecraft
            try
            {
                var crashDir = Path.Combine(PathService.MinecraftDirectory, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    var report = new DirectoryInfo(crashDir).GetFiles("*.txt")
                        .Where(f => f.LastWriteTime >= launchTime.AddSeconds(-5))
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (report != null)
                    {
                        var lines = File.ReadAllLines(report.FullName);
                        var desc = lines.FirstOrDefault(l => l.TrimStart().StartsWith("Description:"));
                        if (desc != null)
                            sb.AppendLine(desc.Trim());
                        sb.AppendLine($"Crash-report: {report.FullName}");
                    }
                }
            }
            catch { }

            // Последние строки с ошибками из лога запуска
            try
            {
                var errLines = File.ReadLines(logPath)
                    .Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                                l.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(3)
                    .ToList();

                if (errLines.Count > 0)
                {
                    sb.AppendLine("Из лога:");
                    foreach (var l in errLines)
                        sb.AppendLine(l.Length > 300 ? l.Substring(0, 300) + "…" : l);
                }
            }
            catch { }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Человекочитаемое описание типичных кодов выхода</summary>
        private static string DescribeExitCode(int code) => code switch
        {
            0 => "нормальное завершение",
            1 => "ошибка игры (чаще всего мод или несовместимость)",
            -1073740791 => "0xC0000409: переполнение стека (обычно краш мода/драйвера)",
            -1073741819 => "0xC0000005: нарушение доступа (краш в нативном коде)",
            -805306369 => "0xCFFFFFCF: повреждение кучи JVM",
            -1073741515 => "0xC0000135: отсутствует нужная DLL/компонент системы",
            -1073740771 => "0xC000040D: аварийное завершение процесса",
            -532462766 => "ошибка Java (проверь путь к Java и версию)",
            _ => "неизвестная ошибка"
        };
    }
}