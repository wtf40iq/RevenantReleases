using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RevenantLauncherSetup.Services;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class SetupWindow : Window
    {
        private readonly SetupViewModel _viewModel;

        public SetupWindow() : this(false, false, null, 0) { }

        public SetupWindow(bool uninstallMode, bool updateMode = false, string? updatePath = null, int launcherProcessId = 0)
        {
            _viewModel = new SetupViewModel(uninstallMode, updateMode, updatePath, launcherProcessId);
            DataContext = _viewModel;
            InitializeComponent();

            if (updateMode)
                Opened += (_, _) => _ = RunUpdateAsync();
        }

        public SetupViewModel ViewModel => _viewModel;

        private async Task RunUpdateAsync()
        {
            try
            {
                await Task.Delay(250);
                await Task.Run(async () =>
                {
                    var installer = new InstallerService();
                    await installer.InstallAsync(_viewModel.BuildOptions(), _viewModel.UpdateProgress);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _viewModel.CurrentPage = SetupPage.Finish;
                        _ = LaunchUpdatedLauncherAsync();
                    });
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _viewModel.InstallFailed = true;
                    _viewModel.ErrorMessage = ex.Message;
                    _viewModel.CurrentPage = SetupPage.Finish;
                });
            }
        }

        private async Task LaunchUpdatedLauncherAsync()
        {
            await Task.Delay(900);
            try
            {
                var launcherPath = System.IO.Path.Combine(_viewModel.InstallPath, "RevenantLauncher.exe");
                if (System.IO.File.Exists(launcherPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcherPath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = _viewModel.InstallPath
                    });
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Update] relaunch failed: {ex.Message}");
            }
            Close();
        }

        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            // Не тянем если клик по кнопке или textbox
            if (e.Source is Control control)
            {
                var el = control;
                while (el != null)
                {
                    if (el is Button || el is TextBox || el is CheckBox || el is ScrollViewer)
                        return;
                    el = el.Parent as Control;
                }
            }

            // Разрешаем перетаскивание только за верхнюю зону (60px)
            var pos = e.GetPosition(this);
            if (pos.Y <= 60)
                BeginMoveDrag(e);
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.CanClose)
                Close();
        }
    }
}