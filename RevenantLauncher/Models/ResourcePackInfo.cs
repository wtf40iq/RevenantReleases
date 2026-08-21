using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace RevenantLauncher.Models
{
    public class ResourcePackInfo : INotifyPropertyChanged
    {
        private bool _hasUpdate;
        private string _updateVersionNumber = "";

        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int PackFormat { get; set; }
        public long FileSizeBytes { get; set; }
        public Bitmap? Icon { get; set; }

        // ===== Данные об обновлении с Modrinth =====

        public string? ModrinthProjectId { get; set; }

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

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
