using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum VersionTab
    {
        Vanilla,
        Forge,
        Fabric
    }

    /// <summary>Пункт списка версий загрузчика (Forge/Fabric) с иконкой</summary>
    public class LoaderVersionItem
    {
        public string Version { get; set; } = "";
        public string IconAsset { get; set; } = "vanilla.png";
        public string IconColor { get; set; } = "#B388FF";
        public Avalonia.Media.Imaging.Bitmap? IconBitmap =>
            Services.AssetImageService.GetIcon(IconAsset);
    }

    public class VersionDialogViewModel : ViewModelBase
    {
        private VersionTab _currentTab = VersionTab.Vanilla;
        private string _searchText = "";
        private bool _showReleases = true;
        private bool _showSnapshots;
        private bool _showOld;
        private bool _onlyInstalled;
        private GameVersion? _selectedVersion;
        private string? _selectedMcVersionForLoader;
        private bool _isLoadingLoaders;
        private string _loaderPanelTitle = "Версия загрузчика";

        public event Action<GameVersion>? VersionSelected;
        public event Action? DialogClosed;

        public ObservableCollection<GameVersion> DisplayedVersions { get; } = new();
        public ObservableCollection<LoaderVersionItem> LoaderVersions { get; } = new();

        public VersionDialogViewModel()
        {
            // Если список версий ещё грузится из сети — обновим список, когда он придёт
            VersionService.Instance.VersionsLoaded += OnVersionsLoaded;
            RefreshList();
        }

        private void OnVersionsLoaded()
        {
            Dispatcher.UIThread.Post(RefreshList);
        }

        public VersionTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsVanillaTab));
                    OnPropertyChanged(nameof(IsForgeTab));
                    OnPropertyChanged(nameof(IsFabricTab));
                    OnPropertyChanged(nameof(ShowFilters));
                    OnPropertyChanged(nameof(IsLoaderMode));
                    SelectedMcVersionForLoader = null;
                    LoaderVersions.Clear();
                    RefreshList();
                }
            }
        }

        public bool IsVanillaTab => CurrentTab == VersionTab.Vanilla;
        public bool IsForgeTab => CurrentTab == VersionTab.Forge;
        public bool IsFabricTab => CurrentTab == VersionTab.Fabric;

        public bool ShowFilters => CurrentTab == VersionTab.Vanilla;
        public bool IsLoaderMode => (IsForgeTab || IsFabricTab) && SelectedMcVersionForLoader != null;

        public string? SelectedMcVersionForLoader
        {
            get => _selectedMcVersionForLoader;
            set
            {
                if (SetProperty(ref _selectedMcVersionForLoader, value))
                    OnPropertyChanged(nameof(IsLoaderMode));
            }
        }

        public string LoaderPanelTitle
        {
            get => _loaderPanelTitle;
            set => SetProperty(ref _loaderPanelTitle, value);
        }

        public bool IsLoadingLoaders
        {
            get => _isLoadingLoaders;
            set => SetProperty(ref _isLoadingLoaders, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RefreshList();
            }
        }

        public bool ShowReleases
        {
            get => _showReleases;
            set
            {
                if (SetProperty(ref _showReleases, value))
                    RefreshList();
            }
        }

        public bool ShowSnapshots
        {
            get => _showSnapshots;
            set
            {
                if (SetProperty(ref _showSnapshots, value))
                    RefreshList();
            }
        }

        public bool ShowOld
        {
            get => _showOld;
            set
            {
                if (SetProperty(ref _showOld, value))
                    RefreshList();
            }
        }

        public bool OnlyInstalled
        {
            get => _onlyInstalled;
            set
            {
                if (SetProperty(ref _onlyInstalled, value))
                    RefreshList();
            }
        }

        public GameVersion? SelectedVersion
        {
            get => _selectedVersion;
            set => SetProperty(ref _selectedVersion, value);
        }

        public void SetTab(VersionTab tab) => CurrentTab = tab;

        public void ToggleOnlyInstalled() => OnlyInstalled = !OnlyInstalled;
        public void ToggleReleases() => ShowReleases = !ShowReleases;
        public void ToggleSnapshots() => ShowSnapshots = !ShowSnapshots;
        public void ToggleOld() => ShowOld = !ShowOld;

        /// <summary>Принудительное обновление списка (вызывается при открытии диалога)</summary>
        public void ForceRefresh()
        {
            SelectedMcVersionForLoader = null;
            LoaderVersions.Clear();
            RefreshList();
        }

        public void Cancel()
        {
            SelectedMcVersionForLoader = null;
            LoaderVersions.Clear();
            DialogClosed?.Invoke();
        }

        public async void OnMcVersionClicked(GameVersion version)
        {
            if (CurrentTab == VersionTab.Vanilla)
            {
                VersionSelected?.Invoke(version);
                DialogClosed?.Invoke();
                return;
            }

            SelectedMcVersionForLoader = version.DisplayName;
            LoaderPanelTitle = CurrentTab == VersionTab.Forge
                ? $"Версия Forge для {version.DisplayName}"
                : $"Версия Fabric для {version.DisplayName}";

            await LoadLoaderVersionsAsync(version.DisplayName);
        }

        public void OnLoaderVersionClicked(LoaderVersionItem item)
            => OnLoaderVersionClicked(item.Version);

        public void OnLoaderVersionClicked(string loaderVersion)
        {
            if (SelectedMcVersionForLoader == null) return;

            var mc = SelectedMcVersionForLoader;
            var result = CurrentTab == VersionTab.Forge
                ? new GameVersion
                {
                    Id = $"{mc}-forge",
                    DisplayName = $"{mc} Forge {loaderVersion}",
                    Type = VersionType.Forge,
                    LoaderVersion = loaderVersion
                }
                : new GameVersion
                {
                    Id = $"{mc}-fabric",
                    DisplayName = $"{mc} Fabric {loaderVersion}",
                    Type = VersionType.Fabric,
                    LoaderVersion = loaderVersion
                };

            VersionService.Instance.RefreshInstallStatus(result);
            VersionSelected?.Invoke(result);
            SelectedMcVersionForLoader = null;
            LoaderVersions.Clear();
            DialogClosed?.Invoke();
        }

        public void UseLatestLoader()
        {
            if (SelectedMcVersionForLoader == null) return;

            var mc = SelectedMcVersionForLoader;
            var result = CurrentTab == VersionTab.Forge
                ? new GameVersion
                {
                    Id = $"{mc}-forge",
                    DisplayName = $"{mc} Forge",
                    Type = VersionType.Forge,
                    LoaderVersion = null
                }
                : new GameVersion
                {
                    Id = $"{mc}-fabric",
                    DisplayName = $"{mc} Fabric",
                    Type = VersionType.Fabric,
                    LoaderVersion = null
                };

            VersionService.Instance.RefreshInstallStatus(result);
            VersionSelected?.Invoke(result);
            SelectedMcVersionForLoader = null;
            LoaderVersions.Clear();
            DialogClosed?.Invoke();
        }

        public void BackToMcList()
        {
            SelectedMcVersionForLoader = null;
            LoaderVersions.Clear();
        }

        private async Task LoadLoaderVersionsAsync(string mcVersion)
        {
            IsLoadingLoaders = true;
            LoaderVersions.Clear();

            try
            {
                List<string> versions;
                if (CurrentTab == VersionTab.Forge)
                    versions = await VersionService.Instance.GetForgeVersionsAsync(mcVersion);
                else
                    versions = await VersionService.Instance.GetFabricLoaderVersionsAsync(mcVersion);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var iconAsset = CurrentTab == VersionTab.Forge ? "forge.png" : "fabric.png";
                    var iconColor = CurrentTab == VersionTab.Forge ? "#FF7043" : "#4DD0E1";

                    foreach (var v in versions.Take(50))
                        LoaderVersions.Add(new LoaderVersionItem
                        {
                            Version = v,
                            IconAsset = iconAsset,
                            IconColor = iconColor
                        });
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LoaderVersions] {ex.Message}");
            }
            finally
            {
                IsLoadingLoaders = false;
            }
        }

        private void RefreshList()
        {
            IEnumerable<GameVersion> source = CurrentTab switch
            {
                VersionTab.Vanilla => VersionService.Instance.VanillaVersions,
                VersionTab.Forge => VersionService.Instance.ForgeVersions,
                VersionTab.Fabric => VersionService.Instance.FabricVersions,
                _ => new List<GameVersion>()
            };

            if (CurrentTab == VersionTab.Vanilla)
            {
                // При фильтре "Установленные" показываем все установленные,
                // независимо от переключателей Release/Snapshot/Old
                if (!OnlyInstalled)
                {
                    source = source.Where(v =>
                        (ShowReleases && v.Type == VersionType.Release) ||
                        (ShowSnapshots && v.Type == VersionType.Snapshot) ||
                        (ShowOld && (v.Type == VersionType.OldBeta || v.Type == VersionType.OldAlpha)));
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                source = source.Where(v =>
                    v.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    v.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            // Обновляем статус установки для отфильтрованных версий (быстро)
            var list = source.Take(200).ToList();
            foreach (var v in list)
                VersionService.Instance.RefreshInstallStatus(v);

            // Фильтр "только установленные"
            if (OnlyInstalled)
                list = list.Where(v => v.IsInstalled).ToList();

            DisplayedVersions.Clear();
            foreach (var v in list)
                DisplayedVersions.Add(v);
        }
    }
}