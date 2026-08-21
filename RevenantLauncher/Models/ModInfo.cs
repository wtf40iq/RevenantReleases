using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace RevenantLauncher.Models
{
    public enum ModLoader
    {
        Unknown,
        Forge,
        Fabric,
        Quilt,
        NeoForge
    }

    public class ModInfo : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private bool _hasUpdate;
        private string _updateVersionNumber = "";

        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string ModId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Authors { get; set; } = string.Empty;
        public string Homepage { get; set; } = string.Empty;
        public ModLoader Loader { get; set; } = ModLoader.Unknown;
        public long FileSizeBytes { get; set; }
        public Bitmap? Icon { get; set; }

        // ===== Данные об обновлении с Modrinth =====

        /// <summary>Project id на Modrinth (если мод распознан)</summary>
        public string? ModrinthProjectId { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
        }

        /// <summary>Доступна ли более новая версия на Modrinth</summary>
        public bool HasUpdate
        {
            get => _hasUpdate;
            set
            {
                if (_hasUpdate != value)
                {
                    _hasUpdate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UpdateBadgeText));
                }
            }
        }

        public string UpdateVersionNumber
        {
            get => _updateVersionNumber;
            set
            {
                if (_updateVersionNumber != value)
                {
                    _updateVersionNumber = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UpdateBadgeText));
                }
            }
        }

        public string UpdateBadgeText => $"Доступно: {UpdateVersionNumber}";

        /// <summary>URL файла обновления (не отображается в UI)</summary>
        public string? UpdateDownloadUrl { get; set; }
        public string? UpdateFileName { get; set; }

        public string FileSizeDisplay
        {
            get
            {
                double kb = FileSizeBytes / 1024.0;
                if (kb < 1024) return $"{kb:F1} KB";
                double mb = kb / 1024.0;
                return $"{mb:F1} MB";
            }
        }

        public string LoaderDisplay => Loader switch
        {
            ModLoader.Forge => "Forge",
            ModLoader.Fabric => "Fabric",
            ModLoader.Quilt => "Quilt",
            ModLoader.NeoForge => "NeoForge",
            _ => "?"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}