using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevenantLauncher.Models
{
    public enum ToastType
    {
        Success,
        Error,
        Warning,
        Info
    }

    public class ToastNotification : INotifyPropertyChanged
    {
        private bool _isVisible = true;
        private double _timeProgress = 100;

        public Guid Id { get; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ToastType Type { get; set; } = ToastType.Info;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int DurationMs { get; set; } = 4000;

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Прогресс времени жизни (100 → 0)</summary>
        public double TimeProgress
        {
            get => _timeProgress;
            set
            {
                if (Math.Abs(_timeProgress - value) > 0.01)
                {
                    _timeProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IconData => Type switch
        {
            ToastType.Success => "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z",
            ToastType.Error => "M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z",
            ToastType.Warning => "M12 2L1 21h22L12 2zm1 15h-2v-2h2v2zm0-4h-2v-4h2v4z",
            ToastType.Info => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-6h2v6zm0-8h-2V9h2v2z",
            _ => ""
        };

        public string IconColor => Type switch
        {
            ToastType.Success => "#66BB6A",
            ToastType.Error => "#FF6666",
            ToastType.Warning => "#FFB74D",
            ToastType.Info => "#4FC3F7",
            _ => "#FFFFFF"
        };

        public string BorderColor => Type switch
        {
            ToastType.Success => "#4066BB6A",
            ToastType.Error => "#40FF6666",
            ToastType.Warning => "#40FFB74D",
            ToastType.Info => "#404FC3F7",
            _ => "#40FFFFFF"
        };

        public string AccentColor => Type switch
        {
            ToastType.Success => "#66BB6A",
            ToastType.Error => "#FF6666",
            ToastType.Warning => "#FFB74D",
            ToastType.Info => "#4FC3F7",
            _ => "#FFFFFF"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}