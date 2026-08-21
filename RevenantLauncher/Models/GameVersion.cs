using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevenantLauncher.Models
{
    public enum VersionType
    {
        Release,
        Snapshot,
        OldBeta,
        OldAlpha,
        Forge,
        Fabric,
        Quilt,
        NeoForge
    }

    public class GameVersion : INotifyPropertyChanged
    {
        private bool _isInstalled;
        private long _installedSizeBytes;

        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public VersionType Type { get; set; } = VersionType.Release;
        public string? LoaderVersion { get; set; }
        public string? InstallPath { get; set; }

        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled != value)
                {
                    _isInstalled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotInstalled));
                }
            }
        }

        public bool IsNotInstalled => !_isInstalled;

        public long InstalledSizeBytes
        {
            get => _installedSizeBytes;
            set
            {
                if (_installedSizeBytes != value)
                {
                    _installedSizeBytes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(InstalledSizeDisplay));
                }
            }
        }

        public string InstalledSizeDisplay
        {
            get
            {
                if (_installedSizeBytes < 1024) return $"{_installedSizeBytes} B";
                double kb = _installedSizeBytes / 1024.0;
                if (kb < 1024) return $"{kb:F1} KB";
                double mb = kb / 1024.0;
                if (mb < 1024) return $"{mb:F1} MB";
                double gb = mb / 1024.0;
                return $"{gb:F2} GB";
            }
        }

        /// <summary>Ассет иконки загрузчика/типа версии</summary>
        public string TypeIconAsset => Type switch
        {
            VersionType.Forge or VersionType.NeoForge => "forge.png",
            VersionType.Fabric or VersionType.Quilt => "fabric.png",
            _ => "vanilla.png"
        };

        /// <summary>Битмап иконки (кешируется сервисом)</summary>
        public Avalonia.Media.Imaging.Bitmap? TypeIconBitmap =>
            Services.AssetImageService.GetIcon(TypeIconAsset);

        /// <summary>Цвет иконки по типу версии</summary>
        public string TypeIconColor => Type switch
        {
            VersionType.Release => "#66BB6A",
            VersionType.Snapshot => "#FFB74D",
            VersionType.OldBeta or VersionType.OldAlpha => "#90A4AE",
            VersionType.Forge => "#FF7043",
            VersionType.NeoForge => "#FF8A65",
            VersionType.Fabric => "#4DD0E1",
            VersionType.Quilt => "#A1887F",
            _ => "#B388FF"
        };

        /// <summary>Короткая подпись типа</summary>
        public string TypeLabel => Type switch
        {
            VersionType.Release => "Release",
            VersionType.Snapshot => "Snapshot",
            VersionType.OldBeta => "Beta",
            VersionType.OldAlpha => "Alpha",
            VersionType.Forge => "Forge",
            VersionType.NeoForge => "NeoForge",
            VersionType.Fabric => "Fabric",
            VersionType.Quilt => "Quilt",
            _ => ""
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}