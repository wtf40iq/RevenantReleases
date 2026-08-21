using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevenantLauncher.Models
{
    public class ModrinthProject : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isInstalled;

        public string ProjectId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public int Downloads { get; set; }
        public int Followers { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public string LatestVersion { get; set; } = string.Empty;

        /// <summary>Установлен ли мод сейчас</summary>
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled != value)
                {
                    _isInstalled = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsInstalled)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsNotInstalled)));
                }
            }
        }

        public bool IsNotInstalled => !_isInstalled;

        public string DownloadsDisplay
        {
            get
            {
                if (Downloads > 1_000_000) return $"{Downloads / 1_000_000.0:F1}M";
                if (Downloads > 1_000) return $"{Downloads / 1_000.0:F1}K";
                return Downloads.ToString();
            }
        }

        public string FollowersDisplay
        {
            get
            {
                if (Followers > 1_000_000) return $"{Followers / 1_000_000.0:F1}M";
                if (Followers > 1_000) return $"{Followers / 1_000.0:F1}K";
                return Followers.ToString();
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class ModrinthProjectDetails : ModrinthProject
    {
        public string Body { get; set; } = string.Empty;
        public string ClientSide { get; set; } = string.Empty;
        public string ServerSide { get; set; } = string.Empty;
        public string ProjectType { get; set; } = string.Empty;
        public string License { get; set; } = string.Empty;
        public string IssuesUrl { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string WikiUrl { get; set; } = string.Empty;
        public string DiscordUrl { get; set; } = string.Empty;
        public List<ModrinthGalleryImage> Gallery { get; set; } = new();
        public string ProjectUrl => $"https://modrinth.com/mod/{Slug}";
    }

    public class ModrinthGalleryImage
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public enum ModDependencyType
    {
        Required,
        Optional,
        Incompatible,
        Embedded
    }

    public class ModrinthDependency
    {
        public string? VersionId { get; set; }
        public string? ProjectId { get; set; }
        public string? FileName { get; set; }
        public ModDependencyType DependencyType { get; set; } = ModDependencyType.Required;
    }

    public class ModrinthVersion : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string VersionId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string VersionNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VersionType { get; set; } = "release";
        public List<string> GameVersions { get; set; } = new();
        public List<string> Loaders { get; set; } = new();
        public string DownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Downloads { get; set; }
        public string DatePublished { get; set; } = string.Empty;
        public List<ModrinthDependency> Dependencies { get; set; } = new();

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public string GameVersionsDisplay => string.Join(", ", GameVersions);
        public string LoadersDisplay => string.Join(", ", Loaders);

        public string FileSizeDisplay
        {
            get
            {
                double kb = FileSize / 1024.0;
                if (kb < 1024) return $"{kb:F1} KB";
                double mb = kb / 1024.0;
                return $"{mb:F1} MB";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class DependencyItem : INotifyPropertyChanged
    {
        private bool _isChecked = true;
        private bool _isExpanded;
        private ModrinthVersion? _resolvedVersion;
        private bool _isAlreadyInstalled;
        private ObservableCollection<ModrinthVersion> _availableVersions = new();

        public string ProjectId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconUrl { get; set; } = string.Empty;
        public ModDependencyType DependencyType { get; set; }

        public ModrinthVersion? ResolvedVersion
        {
            get => _resolvedVersion;
            set
            {
                if (_resolvedVersion != value)
                {
                    _resolvedVersion = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusLabel));
                    OnPropertyChanged(nameof(CanInstall));
                    OnPropertyChanged(nameof(HasMultipleVersions));
                }
            }
        }

        public bool IsAlreadyInstalled
        {
            get => _isAlreadyInstalled;
            set
            {
                if (_isAlreadyInstalled != value)
                {
                    _isAlreadyInstalled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusLabel));
                    OnPropertyChanged(nameof(CanInstall));
                }
            }
        }

        public ObservableCollection<ModrinthVersion> AvailableVersions
        {
            get => _availableVersions;
            set
            {
                _availableVersions = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMultipleVersions));
            }
        }

        public bool HasMultipleVersions => AvailableVersions?.Count > 1;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsRequired => DependencyType == ModDependencyType.Required;
        public bool IsOptional => DependencyType == ModDependencyType.Optional;

        public string TypeLabel => DependencyType switch
        {
            ModDependencyType.Required => "Обязательно",
            ModDependencyType.Optional => "Опционально",
            ModDependencyType.Incompatible => "Несовместимо",
            ModDependencyType.Embedded => "Встроено",
            _ => ""
        };

        public string StatusLabel
        {
            get
            {
                if (IsAlreadyInstalled) return "Уже установлено";
                if (ResolvedVersion == null) return "Не найдено";
                return ResolvedVersion.VersionNumber;
            }
        }

        public bool CanInstall => !IsAlreadyInstalled && ResolvedVersion != null;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}