using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public class ModDetailsDialogViewModel : ViewModelBase
    {
        private ModrinthProject? _project;
        private ModrinthProjectDetails? _details;
        private ModrinthVersion? _selectedVersion;
        private bool _isLoading;
        private bool _isDownloading;
        private string _downloadStatus = "";
        private bool _filterByCurrentVersion = true;
        private string _cleanBodyPreview = "";
        private bool _isResourcePack;

        public event Action? DialogClosed;
        public event Action<ModrinthProject, ModrinthVersion>? RequestDependencyDialog;

        public ObservableCollection<ModrinthVersion> Versions { get; } = new();
        public ObservableCollection<ModrinthVersion> AllVersions { get; } = new();

        public ModrinthProject? Project
        {
            get => _project;
            set
            {
                if (SetProperty(ref _project, value))
                {
                    OnPropertyChanged(nameof(HasProject));
                    OnPropertyChanged(nameof(IconUrl));
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(ShortDescription));
                    OnPropertyChanged(nameof(DownloadsDisplay));
                    OnPropertyChanged(nameof(FollowersDisplay));
                }
            }
        }

        public ModrinthProjectDetails? Details
        {
            get => _details;
            set
            {
                if (SetProperty(ref _details, value))
                {
                    OnPropertyChanged(nameof(HasDetails));
                    OnPropertyChanged(nameof(BodyPreview));
                    OnPropertyChanged(nameof(License));
                    OnPropertyChanged(nameof(HasSource));
                    OnPropertyChanged(nameof(SourceUrl));
                    OnPropertyChanged(nameof(HasIssues));
                    OnPropertyChanged(nameof(IssuesUrl));
                    OnPropertyChanged(nameof(HasWiki));
                    OnPropertyChanged(nameof(WikiUrl));
                    OnPropertyChanged(nameof(HasGallery));
                    OnPropertyChanged(nameof(Gallery));
                    OnPropertyChanged(nameof(Categories));
                    OnPropertyChanged(nameof(CategoriesDisplay));
                    OnPropertyChanged(nameof(ProjectUrl));
                }
            }
        }

        public bool HasProject => Project != null;
        public bool HasDetails => Details != null;
        public string IconUrl => Project?.IconUrl ?? "";
        public string Title => Project?.Title ?? "";
        public string ShortDescription => Project?.Description ?? "";
        public string DownloadsDisplay => Project?.DownloadsDisplay ?? "0";
        public string FollowersDisplay => Project?.FollowersDisplay ?? "0";

        public string BodyPreview => _cleanBodyPreview;
        public string License => Details?.License ?? "";
        public string CategoriesDisplay => Details != null ? string.Join(", ", Details.Categories) : "";
        public ObservableCollection<string> Categories => new(Details?.Categories ?? new());
        public ObservableCollection<ModrinthGalleryImage> Gallery => new(Details?.Gallery ?? new());
        public bool HasGallery => Details?.Gallery.Count > 0;

        public bool HasSource => !string.IsNullOrEmpty(Details?.SourceUrl);
        public string SourceUrl => Details?.SourceUrl ?? "";
        public bool HasIssues => !string.IsNullOrEmpty(Details?.IssuesUrl);
        public string IssuesUrl => Details?.IssuesUrl ?? "";
        public bool HasWiki => !string.IsNullOrEmpty(Details?.WikiUrl);
        public string WikiUrl => Details?.WikiUrl ?? "";
        public string ProjectUrl => _isResourcePack && Details != null
            ? $"https://modrinth.com/resourcepack/{Details.Slug}"
            : Details?.ProjectUrl ?? "";

        public ModrinthVersion? SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                // Снимаем выделение с прошлой
                if (_selectedVersion != null)
                    _selectedVersion.IsSelected = false;

                if (SetProperty(ref _selectedVersion, value))
                    OnPropertyChanged(nameof(CanInstall));

                // Ставим на новую
                if (_selectedVersion != null)
                    _selectedVersion.IsSelected = true;
            }
        }

        public bool CanInstall => SelectedVersion != null && !IsDownloading;

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set
            {
                if (SetProperty(ref _isDownloading, value))
                    OnPropertyChanged(nameof(CanInstall));
            }
        }

        public string DownloadStatus
        {
            get => _downloadStatus;
            set => SetProperty(ref _downloadStatus, value);
        }

        public bool FilterByCurrentVersion
        {
            get => _filterByCurrentVersion;
            set
            {
                if (SetProperty(ref _filterByCurrentVersion, value))
                    RefreshVersions();
            }
        }

        public async Task OpenAsync(ModrinthProject project)
            => await OpenAsync(project, false);

        /// <summary>Открыть диалог; resourcePack=true — установка в папку ресурспаков без фильтра по загрузчику</summary>
        public async Task OpenAsync(ModrinthProject project, bool resourcePack)
        {
            _isResourcePack = resourcePack;
            Project = project;
            Details = null;
            SelectedVersion = null;
            Versions.Clear();
            AllVersions.Clear();
            _cleanBodyPreview = "";
            IsLoading = true;

            try
            {
                var details = await ModrinthService.Instance.GetProjectDetailsAsync(project.ProjectId);
                if (details != null)
                {
                    Details = details;
                    _cleanBodyPreview = CleanMarkdown(details.Body);
                    OnPropertyChanged(nameof(BodyPreview));
                }

                var versions = await ModrinthService.Instance.GetProjectVersionsAsync(project.ProjectId);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AllVersions.Clear();
                    foreach (var v in versions)
                        AllVersions.Add(v);
                    RefreshVersions();
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void RefreshVersions()
        {
            Versions.Clear();

            IEnumerable<ModrinthVersion> src = AllVersions;

            if (FilterByCurrentVersion)
            {
                var current = ConfigService.Instance.Data.Settings.LastSelectedVersion;
                var (mcVer, loader) = ParseCurrentVersion(current);

                if (!string.IsNullOrEmpty(mcVer))
                    src = src.Where(v => v.GameVersions.Contains(mcVer));
                // Ресурспаки не зависят от загрузчика
                if (!string.IsNullOrEmpty(loader) && !_isResourcePack)
                    src = src.Where(v => v.Loaders.Any(l => l.Equals(loader, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var v in src.Take(50))
                Versions.Add(v);

            SelectedVersion = Versions.FirstOrDefault();
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

        public void Close()
        {
            Project = null;
            Details = null;
            Versions.Clear();
            AllVersions.Clear();
            DialogClosed?.Invoke();
        }

        public async Task InstallSelectedAsync()
        {
            if (SelectedVersion == null || Project == null) return;

            // Ресурспаки: без зависимостей, установка в свою папку
            if (_isResourcePack)
            {
                IsDownloading = true;
                DownloadStatus = $"Скачивание {SelectedVersion.FileName}...";
                try
                {
                    using var stream = await ModrinthService.Instance.DownloadModAsync(SelectedVersion.DownloadUrl);
                    await ResourcePackService.Instance.AddFromStreamAsync(stream, SelectedVersion.FileName, Project.ProjectId);

                    DownloadStatus = $"✓ Установлено: {SelectedVersion.FileName}";
                    await Task.Delay(1500);
                    Close();
                }
                catch (Exception ex)
                {
                    DownloadStatus = $"Ошибка: {ex.Message}";
                    await Task.Delay(3000);
                }
                finally
                {
                    IsDownloading = false;
                    DownloadStatus = "";
                }
                return;
            }

            // Проверяем зависимости
            var hasRealDeps = SelectedVersion.Dependencies.Any(d =>
                d.DependencyType == ModDependencyType.Required ||
                d.DependencyType == ModDependencyType.Optional);

            if (hasRealDeps)
            {
                // Просим главный VM открыть диалог зависимостей
                var project = Project;
                var version = SelectedVersion;
                Close();
                RequestDependencyDialog?.Invoke(project, version);
                return;
            }

            IsDownloading = true;
            DownloadStatus = $"Скачивание {SelectedVersion.FileName}...";

            try
            {
                using var stream = await ModrinthService.Instance.DownloadModAsync(SelectedVersion.DownloadUrl);
                await ModService.Instance.AddFromStreamAsync(stream, SelectedVersion.FileName);

                DownloadStatus = $"✓ Установлено: {SelectedVersion.FileName}";
                await Task.Delay(1500);
                Close();
            }
            catch (Exception ex)
            {
                DownloadStatus = $"Ошибка: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                IsDownloading = false;
                DownloadStatus = "";
            }
        }

        public void OpenInBrowser(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>Простая очистка Markdown для превью</summary>
        private static string CleanMarkdown(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";

            var text = md;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"!\[.*?\]\(.*?\)", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[(.*?)\]\(.*?\)", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.+?)_", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }
    }
}