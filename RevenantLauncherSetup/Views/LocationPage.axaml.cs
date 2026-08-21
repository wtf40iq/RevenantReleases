using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RevenantLauncherSetup.Services;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class LocationPage : UserControl
    {
        public LocationPage()
        {
            InitializeComponent();
        }

        private void Back_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SetupViewModel vm)
                vm.GoBack();
        }

        private async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            try
            {
                // Нативный диалог конфликтует с прозрачным окном (зависание/вылет) —
                // на время диалога отключаем прозрачность и возвращаем после
                var oldHints = window.TransparencyLevelHint;
                window.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };

                try
                {
                    var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = "Выбор папки установки",
                        AllowMultiple = false
                    });

                    if (folders.Count > 0 && DataContext is SetupViewModel vm)
                        vm.InstallPath = System.IO.Path.Combine(folders[0].Path.LocalPath, "RevenantLauncher");
                }
                finally
                {
                    window.TransparencyLevelHint = oldHints;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "revenant_setup_crash.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Browse: {ex}\n\n");
                }
                catch { }
            }
        }

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SetupViewModel vm) return;

            vm.GoNext();

            // Запускаем установку в фоне
            await Task.Run(async () =>
            {
                try
                {
                    var options = vm.BuildOptions();
                    var installer = new InstallerService();
                    await installer.InstallAsync(options, vm.UpdateProgress);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.CurrentPage = SetupPage.Finish;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.InstallFailed = true;
                        vm.ErrorMessage = ex.Message;
                        vm.CurrentPage = SetupPage.Finish;
                    });
                }
            });
        }

        private Window? GetTopLevelWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }
}