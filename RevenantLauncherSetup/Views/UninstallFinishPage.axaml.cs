using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace RevenantLauncherSetup.Views
{
    public partial class UninstallFinishPage : UserControl
    {
        public UninstallFinishPage()
        {
            InitializeComponent();
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }
}