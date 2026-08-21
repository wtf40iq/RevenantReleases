using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.Services;

namespace RevenantLauncher.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfo _update;
        private bool _isBusy;

        public UpdateWindow() : this(new UpdateInfo()) { }

        public UpdateWindow(UpdateInfo update)
        {
            _update = update;
            InitializeComponent();

            VersionText.Text = $"v{update.Version}";
            NotesText.Text = string.IsNullOrWhiteSpace(update.Notes)
                ? "Исправления ошибок и улучшения стабильности."
                : update.Notes.Trim();
        }

        private void Later_Click(object? sender, RoutedEventArgs e) => Close();

        private async void Update_Click(object? sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            _isBusy = true;
            UpdateButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            DownloadProgress.IsVisible = true;
            StatusText.Text = "Скачиваем обновление...";

            var installerPath = await UpdateService.DownloadInstallerAsync(
                _update,
                progress => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    DownloadProgress.Value = progress * 100));

            if (string.IsNullOrEmpty(installerPath) || !File.Exists(installerPath))
            {
                StatusText.Text = "Не удалось скачать обновление. Проверь соединение и попробуй позже.";
                DownloadProgress.IsVisible = false;
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                _isBusy = false;
                return;
            }

            try
            {
                var installPath = Path.GetDirectoryName(Environment.ProcessPath ?? "")
                    ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"--update --install-path {QuoteArgument(installPath)} --launcher-pid {Environment.ProcessId}",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
                };

                // Запускаем установщик до закрытия лаунчера: он дождётся освобождения exe.
                Process.Start(startInfo);
                StatusText.Text = "Установщик запущен. Лаунчер сейчас перезапустится...";
                await Task.Delay(250);

                Close();
                if (Owner is Window owner)
                    owner.Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не удалось запустить обновление: {ex.Message}";
                UpdateButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                _isBusy = false;
            }
        }

        private static string QuoteArgument(string value)
            => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
