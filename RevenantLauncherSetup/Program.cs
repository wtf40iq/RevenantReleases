using Avalonia;
using System;
using System.IO;

namespace RevenantLauncherSetup
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Логируем краши, чтобы понимать причину вылетов установщика
            AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => WriteCrashLog(e.Exception);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static void WriteCrashLog(Exception? ex)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "revenant_setup_crash.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}