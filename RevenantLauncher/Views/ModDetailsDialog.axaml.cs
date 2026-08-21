using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.Models;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class ModDetailsDialog : UserControl
    {
        private ModDetailsDialogViewModel ViewModel => (ModDetailsDialogViewModel)DataContext!;

        public ModDetailsDialog()
        {
            InitializeComponent();
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => ViewModel.Close();

        private void VersionSelect_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthVersion v)
                ViewModel.SelectedVersion = v;
        }

        private async void Install_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.InstallSelectedAsync();
        }

        private void OpenModrinth_Click(object? sender, RoutedEventArgs e)
            => ViewModel.OpenInBrowser(ViewModel.ProjectUrl);

        private void OpenSource_Click(object? sender, RoutedEventArgs e)
            => ViewModel.OpenInBrowser(ViewModel.SourceUrl);

        private void OpenIssues_Click(object? sender, RoutedEventArgs e)
            => ViewModel.OpenInBrowser(ViewModel.IssuesUrl);

        private void OpenWiki_Click(object? sender, RoutedEventArgs e)
            => ViewModel.OpenInBrowser(ViewModel.WikiUrl);
    }
}