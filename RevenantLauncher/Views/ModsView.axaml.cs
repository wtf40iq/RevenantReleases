using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RevenantLauncher.Models;
using RevenantLauncher.Services;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class ModsView : UserControl
    {
        private ModsViewModel ViewModel => (ModsViewModel)DataContext!;

        public ModsView()
        {
            InitializeComponent();
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        private void TabInstalled_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(ModsTab.Installed);
        private void TabBrowse_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(ModsTab.Browse);

        private void FilterAll_Click(object? sender, RoutedEventArgs e) => ViewModel.SetFilter(ModFilter.All);
        private void FilterEnabled_Click(object? sender, RoutedEventArgs e) => ViewModel.SetFilter(ModFilter.Enabled);
        private void FilterDisabled_Click(object? sender, RoutedEventArgs e) => ViewModel.SetFilter(ModFilter.Disabled);

        private void OpenFolder_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenModsFolder();

        private async void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.LoadInstalledAsync();
            ViewModel.RefreshBrowseInstallStatus();
            ToastService.Instance.ShowInfo("Список обновлён");
        }

        private void PrevPage_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.PrevPage();
            ScrollBrowseToTop();
        }

        private void NextPage_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.NextPage();
            ScrollBrowseToTop();
        }

        private void PageBtn_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PageItem page && !page.IsEllipsis)
            {
                ViewModel.GoToPage(page.PageNumber);
                ScrollBrowseToTop();
            }
        }

        private void ScrollBrowseToTop()
        {
            var listBox = this.FindControl<ListBox>("BrowseScroller");
            if (listBox != null && listBox.ItemCount > 0)
                listBox.ScrollIntoView(0);
        }

        private async void ExportPack_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var files = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Экспорт сборки",
                SuggestedFileName = "RevenantPack.mrpack",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Modrinth pack (*.mrpack)") { Patterns = new[] { "*.mrpack" } }
                }
            });

            if (files != null)
                await ViewModel.ExportPackAsync(files.Path.LocalPath);
        }

        private async void ImportPack_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Импорт сборки",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Modrinth pack (*.mrpack)") { Patterns = new[] { "*.mrpack" } },
                    new FilePickerFileType("Архив (*.zip)") { Patterns = new[] { "*.zip" } }
                }
            });

            if (files.Count > 0)
                await ViewModel.ImportPackAsync(files[0].Path.LocalPath);
        }

        private async void AddMods_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выбор модов",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Minecraft mods (*.jar)") { Patterns = new[] { "*.jar" } }
                }
            });

            if (files.Count > 0)
                await ViewModel.AddModsAsync(files.Select(f => f.Path.LocalPath).ToList());
        }

        private async void ToggleMod_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts && ts.Tag is ModInfo mod)
                await ViewModel.ToggleAsync(mod);
        }

        private async void RemoveMod_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModInfo mod)
                await ViewModel.RemoveAsync(mod);
        }

        private async void ToggleFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.Tag is ModFolderGroup group)
                await ViewModel.ToggleFolderAsync(group);
        }

        private void ActivateFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control c && c.Tag is ModFolderGroup group)
                ViewModel.ActivateFolder(group);
        }

        private async void UpdateMod_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModInfo mod)
                await ViewModel.UpdateModAsync(mod);
            e.Handled = true;
        }

        private async void InstallMod_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthProject project)
                await ViewModel.InstallFromModrinthAsync(project);
            e.Handled = true;
        }

        private async void RemoveModByProject_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthProject project)
                await ViewModel.RemoveByProjectAsync(project);
            e.Handled = true;
        }

        private async void ModCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border card && card.Tag is ModrinthProject project)
                await ViewModel.OpenDetailsAsync(project);
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            if (!e.Data.Contains(DataFormats.Files)) return;
            var files = e.Data.GetFiles();
            if (files == null) return;
            var paths = files.Select(f => f.Path.LocalPath)
                .Where(p => p.EndsWith(".jar", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (paths.Count > 0) await ViewModel.AddModsAsync(paths);
        }

        private Window? GetTopLevelWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }
}