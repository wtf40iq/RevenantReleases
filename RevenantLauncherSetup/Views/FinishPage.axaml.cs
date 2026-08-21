using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class FinishPage : UserControl
    {
        public FinishPage()
        {
            InitializeComponent();
        }

        private void Launch_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SetupViewModel vm) return;

            try
            {
                var exePath = Path.Combine(vm.InstallPath, "RevenantLauncher.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            catch { }

            CloseApp();
        }

        private void Finish_Click(object? sender, RoutedEventArgs e)
        {
            CloseApp();
        }

        private void CloseApp()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }
}