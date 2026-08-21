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
    public class DependencyDialogViewModel : ViewModelBase
    {
        private ModrinthProject? _mainProject;
        private ModrinthVersion? _mainVersion;
        private bool _isResolving;
        private bool _isInstalling;
        private string _statusText = "";
        private double _progress;

        public event Action<bool>? DialogClosed;

        public ObservableCollection<DependencyItem> Dependencies { get; } = new();

        public string MainTitle => _mainProject?.Title ?? "";
        public string MainVersion => _mainVersion?.VersionNumber ?? "";
        public string MainIconUrl => _mainProject?.IconUrl ?? "";

        public bool HasDependencies => Dependencies.Count > 0;
        public bool HasRequired => Dependencies.Any(d => d.IsRequired);
        public bool HasOptional => Dependencies.Any(d => d.IsOptional);

        public int RequiredCount => Dependencies.Count(d => d.IsRequired && !d.IsAlreadyInstalled);
        public int OptionalCount => Dependencies.Count(d => d.IsOptional && !d.IsAlreadyInstalled);
        public int AlreadyInstalledCount => Dependencies.Count(d => d.IsAlreadyInstalled);

        public string SummaryText
        {
            get
            {
                if (IsResolving) return "Проверка зависимостей...";
                var parts = new List<string>();
                if (RequiredCount > 0) parts.Add($"обязательных: {RequiredCount}");
                if (OptionalCount > 0) parts.Add($"опциональных: {OptionalCount}");
                if (AlreadyInstalledCount > 0) parts.Add($"уже установлено: {AlreadyInstalledCount}");
                return parts.Count > 0
                    ? "Найдено зависимостей — " + string.Join(", ", parts)
                    : "Зависимости не требуются — можно установить только сам мод";
            }
        }

        public bool IsResolving
        {
            get => _isResolving;
            set
            {
                if (SetProperty(ref _isResolving, value))
                {
                    OnPropertyChanged(nameof(CanInstall));
                    OnPropertyChanged(nameof(SummaryText));
                }
            }
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                if (SetProperty(ref _isInstalling, value))
                    OnPropertyChanged(nameof(CanInstall));
            }
        }

        public bool CanInstall => !IsInstalling && !IsResolving && _mainVersion != null;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public async Task OpenAsync(ModrinthProject project, ModrinthVersion version)
        {
            _mainProject = project;
            _mainVersion = version;

            OnPropertyChanged(nameof(MainTitle));
            OnPropertyChanged(nameof(MainVersion));
            OnPropertyChanged(nameof(MainIconUrl));

            Dependencies.Clear();
            StatusText = "";
            Progress = 0;

            await ResolveDependenciesAsync(version);
            OnPropertyChanged(nameof(CanInstall));
        }

        private async Task ResolveDependenciesAsync(ModrinthVersion version)
        {
            IsResolving = true;
            try
            {
                if (version.Dependencies.Count == 0)
                {
                    OnPropertyChanged(nameof(HasDependencies));
                    OnPropertyChanged(nameof(SummaryText));
                    return;
                }

                var projectIds = version.Dependencies
                    .Where(d => !string.IsNullOrEmpty(d.ProjectId))
                    .Select(d => d.ProjectId!)
                    .Distinct()
                    .ToList();

                var projectsInfo = await ModrinthService.Instance.GetProjectsBatchAsync(projectIds);
                var projectMap = projectsInfo.ToDictionary(p => p.ProjectId);

                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, loader) = ParseCurrentVersion(currentVersion);

                foreach (var dep in version.Dependencies)
                {
                    if (dep.DependencyType == ModDependencyType.Embedded ||
                        dep.DependencyType == ModDependencyType.Incompatible)
                        continue;

                    var item = new DependencyItem
                    {
                        DependencyType = dep.DependencyType,
                        ProjectId = dep.ProjectId ?? ""
                    };

                    if (dep.ProjectId != null && projectMap.TryGetValue(dep.ProjectId, out var pInfo))
                    {
                        item.Title = pInfo.Title;
                        item.Description = pInfo.Description;
                        item.IconUrl = pInfo.IconUrl;

                        // 1. Если указан конкретный version_id — берём его, но проверяем
                        //    совместимость с текущей MC-версией и загрузчиком
                        ModrinthVersion? resolved = null;
                        if (!string.IsNullOrEmpty(dep.VersionId))
                        {
                            var candidate = await ModrinthService.Instance.GetVersionAsync(dep.VersionId);
                            if (candidate != null)
                            {
                                bool loaderOk = string.IsNullOrEmpty(loader) ||
                                    candidate.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase));
                                bool mcOk = string.IsNullOrEmpty(mcVer) ||
                                    candidate.GameVersions.Contains(mcVer);

                                if (loaderOk && mcOk)
                                    resolved = candidate;
                                // иначе — предложенная версия для другого загрузчика/MC,
                                // ищем подходящую сами (ниже)
                            }
                        }

                        // 2. Иначе получаем ВСЕ совместимые версии для правильного выбора
                        List<ModrinthVersion> allCompatible = new();
                        if (resolved == null)
                        {
                            // Строгая фильтрация: точная MC + точный loader
                            allCompatible = await ModrinthService.Instance.GetProjectVersionsAsync(
                                dep.ProjectId, mcVer, loader);

                            // Дополнительная проверка на клиенте (Modrinth иногда возвращает нечистые данные)
                            if (!string.IsNullOrEmpty(loader))
                            {
                                allCompatible = allCompatible
                                    .Where(v => v.Loaders.Any(l =>
                                        l.Equals(loader, StringComparison.OrdinalIgnoreCase)))
                                    .ToList();
                            }
                            if (!string.IsNullOrEmpty(mcVer))
                            {
                                allCompatible = allCompatible
                                    .Where(v => v.GameVersions.Contains(mcVer))
                                    .ToList();
                            }

                            // Приоритет: сначала release, потом beta, потом alpha
                            allCompatible = allCompatible
                                .OrderBy(v => VersionTypePriority(v.VersionType))
                                .ToList();

                            resolved = allCompatible.FirstOrDefault();
                        }

                        item.ResolvedVersion = resolved;
                        item.AvailableVersions = new ObservableCollection<ModrinthVersion>(allCompatible);

                        // Проверка установки — по project_id и slug, а не только по имени файла
                        if (!string.IsNullOrEmpty(dep.ProjectId) &&
                            ModService.Instance.IsProjectInstalled(dep.ProjectId, pInfo.Slug))
                        {
                            item.IsAlreadyInstalled = true;
                        }
                        else if (resolved != null)
                        {
                            var fname = resolved.FileName.ToLower();
                            var installedFiles = ModService.Instance.Mods
                                .Select(m => m.FileName.Replace(".disabled", "").ToLower())
                                .ToHashSet();
                            if (installedFiles.Contains(fname))
                                item.IsAlreadyInstalled = true;
                        }
                    }
                    else
                    {
                        item.Title = "Неизвестная зависимость";
                    }

                    item.IsChecked = item.DependencyType == ModDependencyType.Required && !item.IsAlreadyInstalled;

                    Dependencies.Add(item);
                }

                OnPropertyChanged(nameof(HasDependencies));
                OnPropertyChanged(nameof(HasRequired));
                OnPropertyChanged(nameof(HasOptional));
                OnPropertyChanged(nameof(RequiredCount));
                OnPropertyChanged(nameof(OptionalCount));
                OnPropertyChanged(nameof(AlreadyInstalledCount));
                OnPropertyChanged(nameof(SummaryText));
            }
            finally
            {
                IsResolving = false;
            }
        }

        private static int VersionTypePriority(string type) => type?.ToLower() switch
        {
            "release" => 0,
            "beta" => 1,
            "alpha" => 2,
            _ => 3
        };

        public async Task InstallAllAsync()
        {
            if (_mainVersion == null || _mainProject == null) return;

            IsInstalling = true;
            try
            {
                var currentVersion = ConfigService.Instance.Data.Settings.LastSelectedVersion;

                var toInstall = Dependencies
                    .Where(d => d.IsChecked && d.CanInstall)
                    .ToList();

                int total = 1 + toInstall.Count;
                int done = 0;

                // Основной мод: проверяем установку по project_id/slug и ставим в папку текущей версии
                if (ModService.Instance.IsProjectInstalled(_mainProject.ProjectId, _mainProject.Slug))
                {
                    StatusText = $"⚠ {_mainProject.Title} уже установлен";
                    await Task.Delay(1500);
                }
                else
                {
                    StatusText = $"Скачивание {_mainVersion.FileName}...";
                    try
                    {
                        using var mainStream = await ModrinthService.Instance.DownloadModAsync(_mainVersion.DownloadUrl);
                        await ModService.Instance.AddFromStreamAsync(mainStream, _mainVersion.FileName,
                            versionDisplayName: currentVersion,
                            modrinthProjectId: _mainProject.ProjectId);
                    }
                    catch (Exception ex)
                    {
                        StatusText = $"Ошибка основного мода: {ex.Message}";
                        await Task.Delay(3000);
                        return;
                    }
                }

                done++;
                Progress = (double)done / total * 100;

                foreach (var dep in toInstall)
                {
                    if (dep.ResolvedVersion == null) continue;

                    // Зависимость уже установлена — пропускаем, не качаем повторно
                    if (dep.IsAlreadyInstalled ||
                        (!string.IsNullOrEmpty(dep.ProjectId) &&
                         ModService.Instance.IsProjectInstalled(dep.ProjectId)))
                    {
                        done++;
                        Progress = (double)done / total * 100;
                        continue;
                    }

                    StatusText = $"Скачивание {dep.Title} ({dep.ResolvedVersion.VersionNumber})...";
                    try
                    {
                        using var s = await ModrinthService.Instance.DownloadModAsync(dep.ResolvedVersion.DownloadUrl);
                        await ModService.Instance.AddFromStreamAsync(s, dep.ResolvedVersion.FileName,
                            versionDisplayName: currentVersion,
                            modrinthProjectId: dep.ProjectId);
                    }
                    catch (Exception ex)
                    {
                        StatusText = $"Ошибка {dep.Title}: {ex.Message}";
                        await Task.Delay(1500);
                    }
                    done++;
                    Progress = (double)done / total * 100;
                }

                StatusText = "✓ Установка завершена";
                await Task.Delay(1200);
                DialogClosed?.Invoke(true);
            }
            catch (Exception ex)
            {
                StatusText = $"Ошибка: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                IsInstalling = false;
            }
        }

        public void SelectVersionForDependency(DependencyItem dep, ModrinthVersion version)
        {
            dep.ResolvedVersion = version;

            var installedFiles = ModService.Instance.Mods
                .Select(m => m.FileName.Replace(".disabled", "").ToLower())
                .ToHashSet();

            dep.IsAlreadyInstalled = installedFiles.Contains(version.FileName.ToLower());
            dep.IsExpanded = false;
        }

        public void ToggleExpand(DependencyItem dep)
        {
            dep.IsExpanded = !dep.IsExpanded;
        }

        public void Cancel()
        {
            DialogClosed?.Invoke(false);
        }

        private (string? mcVer, string? loader) ParseCurrentVersion(string displayName)
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (null, null);
            var mc = parts[0];
            string? loader = null;
            if (parts.Length >= 2)
            {
                var second = parts[1].ToLower();
                if (second == "fabric" || second == "forge" || second == "quilt" || second == "neoforge")
                    loader = second;
            }
            return (mc, loader);
        }
    }
}