using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RevenantLauncher.Models;
using RevenantLauncher.Services;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class WorldsView : UserControl
    {
        private WorldsViewModel ViewModel => (WorldsViewModel)DataContext!;

        public WorldsView()
        {
            InitializeComponent();
        }

        private void TabWorlds_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(WorldsTab.Worlds);
        private void TabBackups_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(WorldsTab.Backups);
        private void TabScreenshots_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(WorldsTab.Screenshots);

        private async void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshAsync();
            ToastService.Instance.ShowInfo("Список обновлён");
        }

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            switch (ViewModel.CurrentTab)
            {
                case WorldsTab.Backups:
                    WorldService.Instance.OpenBackupsFolder();
                    break;
                case WorldsTab.Screenshots:
                    WorldService.Instance.OpenScreenshotsFolder();
                    break;
                default:
                    WorldService.Instance.OpenSavesFolder();
                    break;
            }
        }

        // ===== Миры =====

        private async void BackupWorld_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WorldInfo world)
                await ViewModel.BackupWorldAsync(world);
            e.Handled = true;
        }

        private void OpenWorldFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WorldInfo world)
                ViewModel.OpenWorldFolder(world);
        }

        private async void DeleteWorld_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is WorldInfo world)
                await ViewModel.DeleteWorldAsync(world);
        }

        // ===== Бэкапы =====

        private async void RestoreBackup_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BackupInfo backup)
                await ViewModel.RestoreBackupAsync(backup);
            e.Handled = true;
        }

        private async void DeleteBackup_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BackupInfo backup)
                await ViewModel.DeleteBackupAsync(backup);
        }

        // ===== Скриншоты =====

        private void Screenshot_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border { Tag: ScreenshotInfo shot })
                ViewModel.OpenLightbox(shot);
        }

        private async void DeleteScreenshot_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ScreenshotInfo shot)
                await ViewModel.DeleteScreenshotAsync(shot);
            e.Handled = true;
        }

        private void LightboxClose_Click(object? sender, RoutedEventArgs e) => ViewModel.CloseLightbox();
        private void LightboxPrev_Click(object? sender, RoutedEventArgs e) => ViewModel.PrevScreenshot();
        private void LightboxNext_Click(object? sender, RoutedEventArgs e) => ViewModel.NextScreenshot();
        private void OpenScreenshotFile_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenScreenshotFile();

        private async void DeleteScreenshotFromLightbox_Click(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedScreenshot != null)
                await ViewModel.DeleteScreenshotAsync(ViewModel.SelectedScreenshot);
        }
    }
}
