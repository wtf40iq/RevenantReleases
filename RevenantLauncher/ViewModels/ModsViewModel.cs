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
    public enum ModsTab
    {
        Installed,
        Browse
    }

    public enum ModFilter
    {
        All,
        Enabled,
        Disabled
    }

    /// <summary>Папка модов одной версии в аккордеоне «Установленные»</summary>
    public class ModFolderGroup : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isActive;
        private int _modCount;
        private bool _isLoading;

        public ModFolderGroup(string folderKey)
        {
            FolderKey = folderKey;
        }

        public string FolderKey { get; }
        public string DisplayName => ModService.PrettifyFolderKey(FolderKey);

        public int ModCount
        {
            get => _modCount;
            set
            {
                if (SetProperty(ref _modCount, value))
                    OnPropertyChanged(nameof(HasMods));
            }
        }

        public bool HasMods => _modCount > 0;
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                    OnPropertyChanged(nameof(ShowEmptyHint));
            }
        }

        public bool ShowEmptyHint => !_isLoading && Mods.Count == 0;
        public void NotifyModsChanged() => OnPropertyChanged(nameof(ShowEmptyHint));
        public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

        public List<ModInfo> AllMods { get; set; } = new();
        public ObservableCollection<ModInfo> Mods { get; } = new();
    }

    public class ModsViewModel : ViewModelBase
    {
        private ModsTab _currentTab = ModsTab.Installed;
        private string _searchText = "";
        private string _browseSearchText = "";
        private ModFilter _filter = ModFilter.All;
        private bool _isLoading;
        private bool _isSearching;
        private bool _isDownloading;
        private string _downloadStatus = "";
        private bool _isDetailsOpen;
        private bool _isDependencyOpen;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private int _totalHits;
        private bool _isFirstLoad = true;
        private bool _isCheckingUpdates;
        private int _updatesAvailable;
        private CancellationTokenSource? _searchCts;
        private readonly SemaphoreSlim _installLock = new(1, 1);

        // Кеш project_id → установлен ли (для быстрой проверки в BrowseResults)
        private readonly HashSet<string> _installedProjectIds = new(StringComparer.OrdinalIgnoreCase);

        private const int PageSize = 30;

        public ObservableCollection<ModInfo> DisplayedMods { get; } = new();
        public ObservableCollection<ModrinthProject> BrowseResults { get; } = new();
        public ObservableCollection<PageItem> Pages { get; } = new();

        /// <summary>Папки модов по версиям (аккордеон на вкладке «Установленные»)</summary>
        public ObservableCollection<ModFolderGroup> VersionFolders { get; } = new();

        public bool HasVersionFolders => VersionFolders.Count > 0;

        /// <summary>Запрос на активацию версии из меню папок модов</summary>
        public event Action<string>? RequestActivateVersion;

        public ModDetailsDialogViewModel DetailsDialog { get; }
        public DependencyDialogViewModel DependencyDialog { get; }

        public ModsViewModel()
        {
            DetailsDialog = new ModDetailsDialogViewModel();
            DetailsDialog.DialogClosed += () => IsDetailsOpen = false;
            DetailsDialog.RequestDependencyDialog += async (project, version) =>
            {
                await OpenDependencyDialogAsync(project, version);
            };

            DependencyDialog = new DependencyDialogViewModel();
            DependencyDialog.DialogClosed += (installed) =>
            {
                IsDependencyOpen = false;
                if (installed)
                {
                    _ = LoadInstalledAsync();
                    RefreshBrowseInstallStatus();
                }
            };
        }

        /// <summary>Смена текущей версии — перезагрузить моды из соответствующей папки</summary>
        public async Task OnVersionChangedAsync(string versionDisplayName)
        {
            ModService.Instance.SetCurrentVersion(versionDisplayName);
            await LoadInstalledAsync();
            await RefreshFoldersAsync();
            RefreshBrowseInstallStatus();
        }

        public async Task InitializeAsync()
        {
            if (!_isFirstLoad) return;
            _isFirstLoad = false;

            // Устанавливаем текущую версию из настроек
            var current = ConfigService.Instance.Data.Settings.LastSelectedVersion;
            ModService.Instance.SetCurrentVersion(current);

            await LoadInstalledAsync();
        }

        public ModsTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsInstalledTab));
                    OnPropertyChanged(nameof(IsBrowseTab));
                    if (value == ModsTab.Browse && BrowseResults.Count == 0)
                        _ = SearchModrinthAsync();
                    if (value == ModsTab.Installed)
                        _ = LoadInstalledAsync();
                }
            }
        }

        public bool IsInstalledTab => CurrentTab == ModsTab.Installed;
        public bool IsBrowseTab => CurrentTab == ModsTab.Browse;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    RefreshInstalledList();
                    RefreshGroupsFilter();
                }
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

        public ModFilter Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(FilterAllActive));
                    OnPropertyChanged(nameof(FilterEnabledActive));
                    OnPropertyChanged(nameof(FilterDisabledActive));
                    RefreshInstalledList();
                    RefreshGroupsFilter();
                }
            }
        }

        public bool FilterAllActive => Filter == ModFilter.All;
        public bool FilterEnabledActive => Filter == ModFilter.Enabled;
        public bool FilterDisabledActive => Filter == ModFilter.Disabled;

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

        public bool IsDetailsOpen { get => _isDetailsOpen; set => SetProperty(ref _isDetailsOpen, value); }
        public bool IsDependencyOpen { get => _isDependencyOpen; set => SetProperty(ref _isDependencyOpen, value); }

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
            ? $"Найдено: {TotalHits} модов, страница {CurrentPage} из {TotalPages}"
            : "";

        public bool HasPrevPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;

        public int TotalMods => ModService.Instance.Mods.Count;
        public int EnabledMods => ModService.Instance.Mods.Count(m => m.IsEnabled);
        public string StatsDisplay => TotalMods == 0 ? "Модов пока нет" : $"{TotalMods} модов, {EnabledMods} включены";

        public void SetTab(ModsTab tab) => CurrentTab = tab;
        public void SetFilter(ModFilter f) => Filter = f;

        public void GoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages)
                CurrentPage = page;
        }

        public void PrevPage() { if (HasPrevPage) CurrentPage--; }
        public void NextPage() { if (HasNextPage) CurrentPage++; }

        public async Task LoadInstalledAsync()
        {
            IsLoading = true;
            try
            {
                await ModService.Instance.LoadAsync();
                RefreshInstalledList();
                RefreshStats();
            }
            finally
            {
                IsLoading = false;
                _ = CheckUpdatesAsync();
                _ = RefreshFoldersAsync();
            }
        }

        private void RefreshInstalledList()
        {
            IEnumerable<ModInfo> source = ModService.Instance.Mods;
            source = Filter switch
            {
                ModFilter.Enabled => source.Where(m => m.IsEnabled),
                ModFilter.Disabled => source.Where(m => !m.IsEnabled),
                _ => source
            };
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                source = source.Where(m =>
                    m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.ModId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    m.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            DisplayedMods.Clear();
            foreach (var m in source) DisplayedMods.Add(m);
        }

        private void RefreshStats()
        {
            OnPropertyChanged(nameof(TotalMods));
            OnPropertyChanged(nameof(EnabledMods));
            OnPropertyChanged(nameof(StatsDisplay));
        }

        /// <summary>Обновляет флаг IsInstalled у карточек Modrinth</summary>
        public void RefreshBrowseInstallStatus()
        {
            foreach (var project in BrowseResults)
            {
                project.IsInstalled = ModService.Instance.IsProjectInstalled(project.ProjectId, project.Slug);
            }
        }

        // ===== Папки модов по версиям (аккордеон) =====

        public async Task RefreshFoldersAsync()
        {
            var keys = ModService.Instance.GetModFolderKeys();
            var activeKey = ModService.Instance.CurrentVersionDisplayName != null
                ? ModService.FolderKeyFromDisplayName(ModService.Instance.CurrentVersionDisplayName)
                : "";

            var old = VersionFolders.ToDictionary(g => g.FolderKey);
            VersionFolders.Clear();

            foreach (var key in keys)
            {
                var group = old.TryGetValue(key, out var g) ? g : new ModFolderGroup(key);
                group.ModCount = ModService.Instance.CountModsInFolder(key);
                group.IsActive = key == activeKey;
                VersionFolders.Add(group);

                if (group.IsExpanded)
                    await LoadGroupAsync(group);
            }

            OnPropertyChanged(nameof(HasVersionFolders));
        }

        public async Task ToggleFolderAsync(ModFolderGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            if (group.IsExpanded)
                await LoadGroupAsync(group);
        }

        public void ActivateFolder(ModFolderGroup group)
            => RequestActivateVersion?.Invoke(group.DisplayName);

        private async Task LoadGroupAsync(ModFolderGroup group)
        {
            group.IsLoading = true;
            try
            {
                group.AllMods = await ModService.Instance.LoadFolderAsync(group.FolderKey);
                ApplyGroupFilter(group);
            }
            finally
            {
                group.IsLoading = false;
            }
        }

        private void ApplyGroupFilter(ModFolderGroup group)
        {
            IEnumerable<ModInfo> source = group.AllMods;

            if (Filter == ModFilter.Enabled) source = source.Where(m => m.IsEnabled);
            else if (Filter == ModFilter.Disabled) source = source.Where(m => !m.IsEnabled);

            var q = SearchText.Trim();
            if (!string.IsNullOrEmpty(q))
                source = source.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

            group.Mods.Clear();
            foreach (var m in source) group.Mods.Add(m);
            group.NotifyModsChanged();
        }

        private void RefreshGroupsFilter()
        {
            foreach (var g in VersionFolders)
                if (g.IsExpanded) ApplyGroupFilter(g);
        }

        public async Task UpdateModAsync(ModInfo mod)
        {
            if (string.IsNullOrEmpty(mod.UpdateDownloadUrl) || string.IsNullOrEmpty(mod.UpdateFileName))
                return;

            var name = mod.Name;
            var newVersion = mod.UpdateVersionNumber;

            IsDownloading = true;
            DownloadStatus = $"Обновление {name} до {newVersion}...";
            try
            {
                using var stream = await ModrinthService.Instance.DownloadModAsync(mod.UpdateDownloadUrl);
                var replaced = await ModService.Instance.ReplaceModAsync(
                    mod, stream, mod.UpdateFileName, mod.ModrinthProjectId);

                if (replaced != null)
                {
                    ToastService.Instance.ShowSuccess("Мод обновлён", $"{name} → {newVersion}");
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

        /// <summary>Пакетная проверка обновлений установленных модов через SHA1-хэши на Modrinth</summary>
        public async Task CheckUpdatesAsync()
        {
            if (IsCheckingUpdates) return;
            IsCheckingUpdates = true;
            try
            {
                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, loader) = ParseCurrentVersion(currentVersion);

                var mods = ModService.Instance.Mods.ToList();
                if (mods.Count == 0 || loader == null || string.IsNullOrEmpty(mcVer))
                {
                    UpdatesAvailable = 0;
                    return;
                }

                // Хэшируем файлы в фоне
                var hashMap = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
                await Task.Run(() =>
                {
                    foreach (var mod in mods)
                    {
                        var hash = ModService.ComputeSha1(mod.FilePath);
                        if (hash != null) hashMap[hash] = mod;
                    }
                });

                if (hashMap.Count == 0) return;

                var updates = await ModrinthService.Instance.GetUpdatesByHashesAsync(
                    hashMap.Keys, new[] { loader }, new[] { mcVer });

                if (updates.Count == 0)
                {
                    foreach (var mod in mods) mod.HasUpdate = false;
                    UpdatesAvailable = 0;
                    return;
                }

                // Modrinth возвращает последнюю версию даже если файл уже актуален —
                // сверяем id установленной версии с id последней
                var current = await ModrinthService.Instance.GetVersionsByHashesAsync(updates.Keys);

                var withUpdate = new HashSet<ModInfo>();
                foreach (var kvp in updates)
                {
                    if (!hashMap.TryGetValue(kvp.Key, out var mod)) continue;
                    if (string.IsNullOrEmpty(kvp.Value.DownloadUrl)) continue;

                    if (current.TryGetValue(kvp.Key, out var installed) &&
                        installed.VersionId == kvp.Value.VersionId)
                        continue; // уже установлена последняя версия

                    mod.ModrinthProjectId = kvp.Value.ProjectId;
                    mod.UpdateVersionNumber = kvp.Value.VersionNumber;
                    mod.UpdateDownloadUrl = kvp.Value.DownloadUrl;
                    mod.UpdateFileName = kvp.Value.FileName;
                    mod.HasUpdate = true;
                    withUpdate.Add(mod);
                }

                foreach (var mod in mods)
                    if (!withUpdate.Contains(mod))
                        mod.HasUpdate = false;

                UpdatesAvailable = withUpdate.Count;

                if (withUpdate.Count > 0)
                    Console.WriteLine($"[ModsVM] Updates available: {withUpdate.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModsVM] CheckUpdates failed: {ex.Message}");
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        public async Task ToggleAsync(ModInfo mod)
        {
            await ModService.Instance.ToggleModAsync(mod);
            RefreshStats();
            var state = mod.IsEnabled ? "включён" : "выключен";
            ToastService.Instance.ShowInfo($"Мод {state}", mod.Name);
        }

        public async Task RemoveAsync(ModInfo mod)
        {
            var name = mod.Name;
            await ModService.Instance.RemoveModAsync(mod);
            RefreshInstalledList();
            RefreshStats();
            RefreshBrowseInstallStatus();
            ToastService.Instance.ShowInfo("Мод удалён", name);
        }

        /// <summary>Удалить мод по modrinth project_id (вызов из карточки Modrinth)</summary>
        public async Task RemoveByProjectAsync(ModrinthProject project)
        {
            var removed = await ModService.Instance.RemoveByProjectIdAsync(project.ProjectId);
            if (removed)
            {
                project.IsInstalled = false;
                RefreshInstalledList();
                RefreshStats();
                RefreshBrowseInstallStatus();
                ToastService.Instance.ShowInfo("Мод удалён", project.Title);
            }
            else
            {
                ToastService.Instance.ShowError("Не удалось удалить", project.Title);
            }
        }

        public async Task AddModsAsync(IEnumerable<string> filePaths)
        {
            IsLoading = true;
            try
            {
                int added = 0;
                foreach (var path in filePaths)
                {
                    if (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        await ModService.Instance.AddModAsync(path);
                        added++;
                    }
                }
                RefreshInstalledList();
                RefreshStats();

                if (added > 0)
                    ToastService.Instance.ShowSuccess("Моды добавлены", $"Установлено: {added}");
            }
            finally { IsLoading = false; }
        }

        public void OpenModsFolder() => ModService.Instance.OpenModsFolder();

        /// <summary>Экспорт текущей сборки в .mrpack</summary>
        public async Task ExportPackAsync(string destPath)
        {
            var version = ConfigService.Instance.Data.Settings.LastSelectedVersion;
            IsDownloading = true;
            DownloadStatus = "Сборка .mrpack...";
            try
            {
                var packName = $"{version} · Revenant Pack";
                var result = await ModpackService.ExportMrpackAsync(version, destPath, packName);
                ToastService.Instance.ShowSuccess("Сборка экспортирована",
                    $"{result.IndexedMods} модов с Modrinth, {result.OverrideMods} в overrides");
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка экспорта", ex.Message);
            }
            finally
            {
                IsDownloading = false;
                DownloadStatus = "";
            }
        }

        /// <summary>Импорт сборки из .mrpack</summary>
        public async Task ImportPackAsync(string packPath)
        {
            IsDownloading = true;
            try
            {
                var result = await ModpackService.ImportMrpackAsync(packPath,
                    s => Dispatcher.UIThread.Post(() => DownloadStatus = s));

                var loaderText = string.IsNullOrEmpty(result.Loader)
                    ? "Vanilla"
                    : char.ToUpper(result.Loader[0]) + result.Loader.Substring(1);
                ToastService.Instance.ShowSuccess("Сборка импортирована",
                    $"«{result.PackName}» · {result.McVersion} {loaderText} · файлов: {result.Downloaded + result.Overridden}");

                await LoadInstalledAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка импорта", ex.Message);
            }
            finally
            {
                IsDownloading = false;
                DownloadStatus = "";
            }
        }

        public async Task OpenDetailsAsync(ModrinthProject project)
        {
            IsDetailsOpen = true;
            await DetailsDialog.OpenAsync(project);
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
                var (mcVer, loader) = ParseCurrentVersion(currentVersion);

                int offset = (_currentPage - 1) * PageSize;

                var (results, totalHits) = await ModrinthService.Instance.SearchModsWithPaginationAsync(
                    BrowseSearchText,
                    mcVersion: mcVer,
                    loader: loader,
                    limit: PageSize,
                    offset: offset);

                TotalHits = totalHits;
                TotalPages = Math.Max(1, (int)Math.Ceiling((double)totalHits / PageSize));

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BrowseResults.Clear();
                    foreach (var r in results)
                    {
                        r.IsInstalled = ModService.Instance.IsProjectInstalled(r.ProjectId, r.Slug);
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
                // ⚠️ Если уже установлен — не качаем повторно (по project_id и slug)
                if (ModService.Instance.IsProjectInstalled(project.ProjectId, project.Slug))
                {
                    project.IsInstalled = true;
                    ToastService.Instance.ShowWarning("Уже установлен", project.Title);
                    return;
                }

                IsDownloading = true;
                DownloadStatus = $"Поиск версии для {project.Title}...";

                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, loader) = ParseCurrentVersion(currentVersion);

                // СТРОГИЙ поиск: только точное совпадение MC + loader
                var versions = await ModrinthService.Instance.GetProjectVersionsAsync(
                    project.ProjectId, mcVer, loader);

                // Клиентская фильтрация (Modrinth иногда возвращает неточные результаты)
                if (!string.IsNullOrEmpty(loader))
                {
                    versions = versions.Where(v =>
                        v.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase))).ToList();
                }
                if (!string.IsNullOrEmpty(mcVer))
                {
                    versions = versions.Where(v => v.GameVersions.Contains(mcVer)).ToList();
                }

                // Приоритет: release → beta → alpha
                versions = versions.OrderBy(v => VersionTypePriority(v.VersionType)).ToList();

                if (versions.Count == 0)
                {
                    ToastService.Instance.ShowError("Нет версий",
                        $"Не найдено совместимых версий для {mcVer} {loader}");
                    return;
                }

                var version = versions[0];
                if (string.IsNullOrEmpty(version.DownloadUrl))
                {
                    ToastService.Instance.ShowError("Ошибка", $"Файл не найден ({project.Title})");
                    return;
                }

                // Проверяем зависимости
                var hasRealDeps = version.Dependencies.Any(d =>
                    d.DependencyType == ModDependencyType.Required ||
                    d.DependencyType == ModDependencyType.Optional);

                if (hasRealDeps)
                {
                    IsDownloading = false;
                    DownloadStatus = "";
                    _installLock.Release();
                    await OpenDependencyDialogAsync(project, version);
                    return;
                }

                DownloadStatus = $"Скачивание {version.FileName}...";
                using var stream = await ModrinthService.Instance.DownloadModAsync(version.DownloadUrl);
                await ModService.Instance.AddFromStreamAsync(stream, version.FileName,
                    versionDisplayName: currentVersion,
                    modrinthProjectId: project.ProjectId);

                project.IsInstalled = true;
                ToastService.Instance.ShowSuccess("Мод установлен", project.Title);
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

        public async Task OpenDependencyDialogAsync(ModrinthProject project, ModrinthVersion version)
        {
            IsDependencyOpen = true;
            await DependencyDialog.OpenAsync(project, version);
        }

        private static int VersionTypePriority(string type) => type?.ToLower() switch
        {
            "release" => 0,
            "beta" => 1,
            "alpha" => 2,
            _ => 3
        };

        private (string? mcVer, string? loader) ParseCurrentVersion(string displayName)
        {
            var (mc, loader) = PathService.ParseDisplayName(displayName);
            return (mc, loader == "vanilla" ? null : loader);
        }
    }

    public class PageItem : ViewModelBase
    {
        private bool _isCurrent;
        public int PageNumber { get; set; }
        public string DisplayText { get; set; } = "";
        public bool IsEllipsis => PageNumber == -1;

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }
    }
}