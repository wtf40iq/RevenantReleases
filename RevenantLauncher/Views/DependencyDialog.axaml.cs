using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.Models;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class DependencyDialog : UserControl
    {
        private DependencyDialogViewModel ViewModel => (DependencyDialogViewModel)DataContext!;

        public DependencyDialog()
        {
            InitializeComponent();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => ViewModel.Cancel();

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.InstallAllAsync();
        }

        private void ExpandVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DependencyItem dep)
                ViewModel.ToggleExpand(dep);
        }

        private void SelectDepVersion_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthVersion ver)
            {
                // Находим родительский DependencyItem
                var parent = btn.DataContext;
                var current = btn.Parent;
                while (current != null)
                {
                    if (current is Control ctrl && ctrl.DataContext is DependencyItem dep)
                    {
                        ViewModel.SelectVersionForDependency(dep, ver);
                        return;
                    }
                    current = (current as Control)?.Parent;
                }
            }
        }
    }
}