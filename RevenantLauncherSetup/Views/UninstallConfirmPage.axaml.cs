using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using RevenantLauncherSetup.Services;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class UninstallConfirmPage : UserControl
    {
        public UninstallConfirmPage()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }

        private async void Uninstall_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SetupViewModel vm) return;

            vm.CurrentPage = SetupPage.Uninstalling;

            await Task.Run(async () =>
            {
                try
                {
                    var installer = new InstallerService();
                    await installer.UninstallAsync(vm.DeleteUserData, vm.UpdateProgress);

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.CurrentPage = SetupPage.UninstallFinish;
                    });
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        vm.InstallFailed = true;
                        vm.ErrorMessage = ex.Message;
                        vm.CurrentPage = SetupPage.UninstallFinish;
                    });
                }
            });
        }
    }
}