using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RevenantLauncher.Services;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class SettingsView : UserControl
    {
        private SettingsViewModel ViewModel => (SettingsViewModel)DataContext!;

        public SettingsView()
        {
            InitializeComponent();

            // Когда страница полностью подготовлена — принудительно устанавливаем значения Slider'ов
            this.AttachedToVisualTree += async (_, _) =>
            {
                await Task.Delay(300);
                Dispatcher.UIThread.Post(() =>
                {
                    ForceUpdateSliders();
                });
            };
        }

        /// <summary>Принудительно устанавливает значения слайдеров из конфига (обход бага с XAML init)</summary>
        private void ForceUpdateSliders()
        {
            try
            {
                var settings = ConfigService.Instance.Data.Settings;

                var maxRamSlider = this.FindControl<Slider>("MaxRamSlider");
                var minRamSlider = this.FindControl<Slider>("MinRamSlider");
                var volumeSlider = this.FindControl<Slider>("VolumeSlider");

                if (maxRamSlider != null)
                {
                    maxRamSlider.Value = settings.MaxRamMb;
                    Console.WriteLine($"[SettingsView] Forced MaxRamSlider = {settings.MaxRamMb}");
                }

                if (minRamSlider != null)
                {
                    minRamSlider.Value = settings.MinRamMb;
                    Console.WriteLine($"[SettingsView] Forced MinRamSlider = {settings.MinRamMb}");
                }

                if (volumeSlider != null)
                {
                    volumeSlider.Value = settings.SoundsVolume * 100;
                    Console.WriteLine($"[SettingsView] Forced VolumeSlider = {settings.SoundsVolume * 100}");
                }

                // Перезагружаем VM чтобы сбросить HasUnsavedChanges
                ViewModel?.LoadFromConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsView] ForceUpdateSliders error: {ex.Message}");
            }
        }

        private async void BrowseJava_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выбор Java",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Java executable")
                    {
                        Patterns = new[] { "javaw.exe", "java.exe", "java" }
                    }
                }
            });

            if (files.Count > 0)
                ViewModel.JavaPath = files[0].Path.LocalPath;
        }

        private async void BrowseGameDir_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Выбор папки Minecraft",
                AllowMultiple = false
            });

            if (folders.Count > 0)
                ViewModel.GameDirectory = folders[0].Path.LocalPath;
        }

        private void OpenGameFolder_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenGameFolder();
        private void OpenLauncher_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenLauncherFolder();
        private void OpenLogs_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenLogsFolder();

        private async void ResetDefaults_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.ResetToDefaultsAsync();
            // После сброса тоже обновляем слайдеры
            await Task.Delay(100);
            ForceUpdateSliders();
        }

        private void TestSound_Click(object? sender, RoutedEventArgs e) => ViewModel.TestSound();

        private async void SaveAll_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.SaveAllAsync();
        }

        private Window? GetTopLevelWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }
}