using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum ResourcePacksTab
    {
        Installed,
        Browse
    }

    public class ResourcePacksViewModel : ViewModelBase
    {
        private ResourcePacksTab _currentTab = ResourcePacksTab.Installed;
        private string _searchText = "";
        private string _browseSearchText = "";
        private bool _isLoading;
        private bool _isSearching;
        private bool _isDownloading;
        private string _downloadStatus = "";
        private bool _isDetailsOpen;
        private bool _isCheckingUpdates;
        private int _updatesAvailable;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalHits;
        private bool _isFirstLoad = true;
        private CancellationTokenSource? _searchCts;
        private readonly SemaphoreSlim _installLock = new(1, 1);

        private const int PageSize = 30;

        public ObservableCollection<ResourcePackInfo> DisplayedPacks { get; } = new();
        public ObservableCollection<ModrinthProject> BrowseResults { get; } = new();
        public ObservableCollection<PageItem> Pages { get; } = new();

        public ModDetailsDialogViewModel DetailsDialog { get; }

        public ResourcePacksViewModel()
        {
            DetailsDialog = new ModDetailsDialogViewModel();
            DetailsDialog.DialogClosed += () =>
            {
                IsDetailsOpen = false;
                RefreshBrowseInstallStatus();
                _ = LoadInstalledAsync();
            };
        }

        public bool IsDetailsOpen { get => _isDetailsOpen; set => SetProperty(ref _isDetailsOpen, value); }

        public ResourcePacksTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsInstalledTab));
                    OnPropertyChanged(nameof(IsBrowseTab));
                    if (value == ResourcePacksTab.Browse && BrowseResults.Count == 0)
                        _ = SearchModrinthAsync();
                    if (value == ResourcePacksTab.Installed)
                        _ = LoadInstalledAsync();
                }
            }
        }

        public bool IsInstalledTab => CurrentTab == ResourcePacksTab.Installed;
        public bool IsBrowseTab => CurrentTab == ResourcePacksTab.Browse;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RefreshInstalledList();
            }
        }

        public string BrowseSearchText
        {
            get => _browseSearchText;
            set
            {
                if (SetProperty(ref _browseSearchText, value))
                {
                    _currentPage = 1;
                    _ = DebounceSearchAsync();
                }
            }
        }

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsSearching { get => _isSearching; set => SetProperty(ref _isSearching, value); }
        public bool IsDownloading { get => _isDownloading; set => SetProperty(ref _isDownloading, value); }
        public string DownloadStatus { get => _downloadStatus; set => SetProperty(ref _downloadStatus, value); }

        public bool IsCheckingUpdates { get => _isCheckingUpdates; set => SetProperty(ref _isCheckingUpdates, value); }

        public int UpdatesAvailable
        {
            get => _updatesAvailable;
            set
            {
                if (SetProperty(ref _updatesAvailable, value))
                    OnPropertyChanged(nameof(HasUpdatesAvailable));
            }
        }

        public bool HasUpdatesAvailable => _updatesAvailable > 0;

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(PageInfoText));
                    _ = SearchModrinthAsync();
                }
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (SetProperty(ref _totalPages, value))
                    OnPropertyChanged(nameof(PageInfoText));
            }
        }

        public int TotalHits
        {
            get => _totalHits;
            set
            {
                if (SetProperty(ref _totalHits, value))
                    OnPropertyChanged(nameof(PageInfoText));
            }
        }

        public string PageInfoText => TotalHits > 0
            ? $"Найдено: {TotalHits} ресурспаков, страница {CurrentPage} из {TotalPages}"
            : "";

        public bool HasPrevPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;

        public int TotalPacks => ResourcePackService.Instance.Packs.Count;
        public string StatsDisplay => TotalPacks == 0 ? "Ресурспаков пока нет" : $"{TotalPacks} ресурспаков";

        public void SetTab(ResourcePacksTab tab) => CurrentTab = tab;

        public void GoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages)
                CurrentPage = page;
        }

        public void PrevPage() { if (HasPrevPage) CurrentPage--; }
        public void NextPage() { if (HasNextPage) CurrentPage++; }

        public async Task InitializeAsync()
        {
            if (!_isFirstLoad) return;
            _isFirstLoad = false;
            await LoadInstalledAsync();
        }

        public async Task LoadInstalledAsync()
        {
            IsLoading = true;
            try
            {
                await ResourcePackService.Instance.LoadAsync();
                RefreshInstalledList();
                RefreshStats();
            }
            finally
            {
                IsLoading = false;
                _ = CheckUpdatesAsync();
            }
        }

        private void RefreshInstalledList()
        {
            IEnumerable<ResourcePackInfo> source = ResourcePackService.Instance.Packs;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                source = source.Where(p =>
                    p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            DisplayedPacks.Clear();
            foreach (var p in source) DisplayedPacks.Add(p);
        }

        private void RefreshStats()
        {
            OnPropertyChanged(nameof(TotalPacks));
            OnPropertyChanged(nameof(StatsDisplay));
        }

        /// <summary>Обновляет флаг IsInstalled у карточек Modrinth</summary>
        private void RefreshBrowseInstallStatus()
        {
            foreach (var project in BrowseResults)
                project.IsInstalled = ResourcePackService.Instance.IsProjectInstalled(project.ProjectId, project.Slug);
        }

        public async Task RemoveAsync(ResourcePackInfo pack)
        {
            var name = pack.Name;
            await ResourcePackService.Instance.RemoveAsync(pack);
            RefreshInstalledList();
            RefreshStats();
            RefreshBrowseInstallStatus();
            ToastService.Instance.ShowInfo("Ресурспак удалён", name);
        }

        public async Task RemoveByProjectAsync(ModrinthProject project)
        {
            var removed = await ResourcePackService.Instance.RemoveByProjectIdAsync(project.ProjectId);
            if (removed)
            {
                project.IsInstalled = false;
                RefreshInstalledList();
                RefreshStats();
                RefreshBrowseInstallStatus();
                ToastService.Instance.ShowInfo("Ресурспак удалён", project.Title);
            }
            else
            {
                ToastService.Instance.ShowError("Не удалось удалить", project.Title);
            }
        }

        public async Task AddPacksAsync(IEnumerable<string> filePaths)
        {
            IsLoading = true;
            try
            {
                int added = 0;
                foreach (var path in filePaths)
                {
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        await ResourcePackService.Instance.AddPackAsync(path);
                        added++;
                    }
                }
                RefreshInstalledList();
                RefreshStats();

                if (added > 0)
                    ToastService.Instance.ShowSuccess("Ресурспаки добавлены", $"Установлено: {added}");
            }
            finally { IsLoading = false; }
        }

        public void OpenFolder() => ResourcePackService.Instance.OpenFolder();

        public async Task UpdatePackAsync(ResourcePackInfo pack)
        {
            if (string.IsNullOrEmpty(pack.UpdateDownloadUrl) || string.IsNullOrEmpty(pack.UpdateFileName))
                return;

            var name = pack.Name;
            var newVersion = pack.UpdateVersionNumber;

            IsDownloading = true;
            DownloadStatus = $"Обновление {name} до {newVersion}...";
            try
            {
                using var stream = await ModrinthService.Instance.DownloadModAsync(pack.UpdateDownloadUrl);
                var replaced = await ResourcePackService.Instance.ReplacePackAsync(
                    pack, stream, pack.UpdateFileName, pack.ModrinthProjectId);

                if (replaced != null)
                {
                    ToastService.Instance.ShowSuccess("Ресурспак обновлён", $"{name} → {newVersion}");
                    RefreshInstalledList();
                    RefreshStats();
                    RefreshBrowseInstallStatus();
                    _ = CheckUpdatesAsync();
                }
                else
                {
                    ToastService.Instance.ShowError("Ошибка обновления", name);
                }
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка обновления", ex.Message);
            }
            finally
            {
                IsDownloading = false;
                DownloadStatus = "";
            }
        }

        /// <summary>Пакетная проверка обновлений ресурспаков через SHA1-хэши на Modrinth</summary>
        public async Task CheckUpdatesAsync()
        {
            if (IsCheckingUpdates) return;
            IsCheckingUpdates = true;
            try
            {
                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, _) = PathService.ParseDisplayName(currentVersion);

                var packs = ResourcePackService.Instance.Packs.ToList();
                if (packs.Count == 0 || string.IsNullOrEmpty(mcVer))
                {
                    UpdatesAvailable = 0;
                    return;
                }

                var hashMap = new Dictionary<string, ResourcePackInfo>(StringComparer.OrdinalIgnoreCase);
                await Task.Run(() =>
                {
                    foreach (var pack in packs)
                    {
                        var hash = ResourcePackService.ComputeSha1(pack.FilePath);
                        if (hash != null) hashMap[hash] = pack;
                    }
                });

                if (hashMap.Count == 0) return;

                // Ресурспаки не зависят от загрузчика — loaders не передаём
                var updates = await ModrinthService.Instance.GetUpdatesByHashesAsync(
                    hashMap.Keys, null, new[] { mcVer });

                if (updates.Count == 0)
                {
                    foreach (var pack in packs) pack.HasUpdate = false;
                    UpdatesAvailable = 0;
                    return;
                }

                // Сверяем id установленной версии с id последней
                var current = await ModrinthService.Instance.GetVersionsByHashesAsync(updates.Keys);

                var withUpdate = new HashSet<ResourcePackInfo>();
                foreach (var kvp in updates)
                {
                    if (!hashMap.TryGetValue(kvp.Key, out var pack)) continue;
                    if (string.IsNullOrEmpty(kvp.Value.DownloadUrl)) continue;

                    if (current.TryGetValue(kvp.Key, out var installed) &&
                        installed.VersionId == kvp.Value.VersionId)
                        continue; // уже установлена последняя версия

                    pack.ModrinthProjectId = kvp.Value.ProjectId;
                    pack.UpdateVersionNumber = kvp.Value.VersionNumber;
                    pack.UpdateDownloadUrl = kvp.Value.DownloadUrl;
                    pack.UpdateFileName = kvp.Value.FileName;
                    pack.HasUpdate = true;
                    withUpdate.Add(pack);
                }

                foreach (var pack in packs)
                    if (!withUpdate.Contains(pack))
                        pack.HasUpdate = false;

                UpdatesAvailable = withUpdate.Count;

                if (withUpdate.Count > 0)
                    Console.WriteLine($"[ResourcePacksVM] Updates available: {withUpdate.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResourcePacksVM] CheckUpdates failed: {ex.Message}");
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        public async Task OpenDetailsAsync(ModrinthProject project)
        {
            IsDetailsOpen = true;
            await DetailsDialog.OpenAsync(project, resourcePack: true);
        }

        private async Task DebounceSearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            try
            {
                await Task.Delay(600, token);
                await SearchModrinthAsync();
            }
            catch (TaskCanceledException) { }
        }

        private async Task SearchModrinthAsync()
        {
            IsSearching = true;
            try
            {
                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, _) = PathService.ParseDisplayName(currentVersion);

                int offset = (_currentPage - 1) * PageSize;

                var (results, totalHits) = await ModrinthService.Instance.SearchModsWithPaginationAsync(
                    BrowseSearchText,
                    mcVersion: mcVer,
                    loader: null,
                    limit: PageSize,
                    offset: offset,
                    projectType: "resourcepack");

                TotalHits = totalHits;
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalHits / PageSize));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BrowseResults.Clear();
                    foreach (var r in results)
                    {
                        r.IsInstalled = ResourcePackService.Instance.IsProjectInstalled(r.ProjectId, r.Slug);
                        BrowseResults.Add(r);
                    }

                    RefreshPages();
                    OnPropertyChanged(nameof(HasPrevPage));
                    OnPropertyChanged(nameof(HasNextPage));
                });
            }
            finally { IsSearching = false; }
        }

        private void RefreshPages()
        {
            Pages.Clear();
            if (TotalPages <= 1) return;

            var pageNumbers = new HashSet<int> { 1, TotalPages };
            for (int i = Math.Max(1, _currentPage - 2); i <= Math.Min(TotalPages, _currentPage + 2); i++)
                pageNumbers.Add(i);

            var sorted = pageNumbers.OrderBy(p => p).ToList();
            int? prev = null;
            foreach (var p in sorted)
            {
                if (prev != null && p - prev > 1)
                    Pages.Add(new PageItem { PageNumber = -1, DisplayText = "...", IsCurrent = false });

                Pages.Add(new PageItem
                {
                    PageNumber = p,
                    DisplayText = p.ToString(),
                    IsCurrent = p == _currentPage
                });
                prev = p;
            }
        }

        public async Task InstallFromModrinthAsync(ModrinthProject project)
        {
            if (!await _installLock.WaitAsync(0))
            {
                ToastService.Instance.ShowWarning("Подожди", "Дождитесь завершения предыдущей установки");
                return;
            }

            try
            {
                if (ResourcePackService.Instance.IsProjectInstalled(project.ProjectId, project.Slug))
                {
                    project.IsInstalled = true;
                    ToastService.Instance.ShowWarning("Уже установлен", project.Title);
                    return;
                }

                IsDownloading = true;
                DownloadStatus = $"Поиск версии для {project.Title}...";

                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, _) = PathService.ParseDisplayName(currentVersion);

                // Ресурспаки не зависят от загрузчика — фильтруем только по версии MC
                var versions = await ModrinthService.Instance.GetProjectVersionsAsync(project.ProjectId, mcVer, null);
                versions = versions.Where(v => v.GameVersions.Contains(mcVer)).ToList();
                versions = versions.OrderBy(v => VersionTypePriority(v.VersionType)).ToList();

                if (versions.Count == 0)
                {
                    ToastService.Instance.ShowError("Нет версий", $"Не найдено совместимых версий для {mcVer}");
                    return;
                }

                var version = versions[0];
                if (string.IsNullOrEmpty(version.DownloadUrl))
                {
                    ToastService.Instance.ShowError("Ошибка", $"Файл не найден ({project.Title})");
                    return;
                }

                DownloadStatus = $"Скачивание {version.FileName}...";
                using var stream = await ModrinthService.Instance.DownloadModAsync(version.DownloadUrl);
                await ResourcePackService.Instance.AddFromStreamAsync(stream, version.FileName, project.ProjectId);

                project.IsInstalled = true;
                ToastService.Instance.ShowSuccess("Ресурспак установлен", project.Title);
                RefreshStats();
                RefreshBrowseInstallStatus();
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка установки", ex.Message);
            }
            finally
            {
                if (_installLock.CurrentCount == 0)
                    _installLock.Release();
                IsDownloading = false;
                DownloadStatus = "";
            }
        }

        private static int VersionTypePriority(string type) => type?.ToLower() switch
        {
            "release" => 0,
            "beta" => 1,
            "alpha" => 2,
            _ => 3
        };
    }
}
