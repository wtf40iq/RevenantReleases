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
    public partial class ResourcePacksView : UserControl
    {
        private ResourcePacksViewModel ViewModel => (ResourcePacksViewModel)DataContext!;

        public ResourcePacksView()
        {
            InitializeComponent();
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        private void TabInstalled_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(ResourcePacksTab.Installed);
        private void TabBrowse_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(ResourcePacksTab.Browse);

        private void OpenFolder_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenFolder();

        private async void Refresh_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.LoadInstalledAsync();
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

        private async void AddPacks_Click(object? sender, RoutedEventArgs e)
        {
            var window = GetTopLevelWindow();
            if (window == null) return;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выбор ресурспаков",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Resource packs (*.zip)") { Patterns = new[] { "*.zip" } }
                }
            });

            if (files.Count > 0)
                await ViewModel.AddPacksAsync(files.Select(f => f.Path.LocalPath).ToList());
        }

        private async void RemovePack_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ResourcePackInfo pack)
                await ViewModel.RemoveAsync(pack);
        }

        private async void UpdatePack_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ResourcePackInfo pack)
                await ViewModel.UpdatePackAsync(pack);
            e.Handled = true;
        }

        private async void InstallPack_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthProject project)
                await ViewModel.InstallFromModrinthAsync(project);
            e.Handled = true;
        }

        private async void RemovePackByProject_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ModrinthProject project)
                await ViewModel.RemoveByProjectAsync(project);
            e.Handled = true;
        }

        private async void PackCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Клики по кнопкам установки/удаления не открывают диалог
            if (e.Source is Control source)
            {
                var el = source;
                while (el != null)
                {
                    if (el is Button) return;
                    el = el.Parent as Control;
                }
            }

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
                .Where(p => p.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (paths.Count > 0) await ViewModel.AddPacksAsync(paths);
        }

        private Window? GetTopLevelWindow()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }
}
