using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace RevenantLauncher.Models
{
    public class WorldInfo : INotifyPropertyChanged
    {
        public string FolderName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastPlayed { get; set; }
        public int BackupCount { get; set; }
        public Bitmap? Icon { get; set; }

        public string SizeDisplay
        {
            get
            {
                double mb = SizeBytes / 1024.0 / 1024.0;
                if (mb < 1024) return $"{mb:F1} MB";
                return $"{mb / 1024.0:F2} GB";
            }
        }

        public string LastPlayedDisplay => LastPlayed.ToString("dd.MM.yyyy HH:mm");

        public bool HasBackups => BackupCount > 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class BackupInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string WorldName { get; set; } = string.Empty;
        public string Stamp { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime Created { get; set; }

        public string SizeDisplay
        {
            get
            {
                double mb = SizeBytes / 1024.0 / 1024.0;
                if (mb < 1024) return $"{mb:F1} MB";
                return $"{mb / 1024.0:F2} GB";
            }
        }

        public string CreatedDisplay => Created.ToString("dd.MM.yyyy HH:mm");
    }

    public class ScreenshotInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public DateTime Date { get; set; }
        public Bitmap? Thumb { get; set; }

        public string DateDisplay => Date.ToString("dd.MM.yyyy HH:mm");
    }
}
