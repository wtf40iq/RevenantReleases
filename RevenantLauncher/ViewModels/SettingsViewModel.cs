using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private int _minRam;
        private int _maxRam;
        private int _totalSystemRam;
        private string _javaPath = "";
        private bool _autoDetectJava;
        private string _jvmArgs = "";
        private string _gameDirectory = "";
        private int _gameWindowWidth;
        private int _gameWindowHeight;
        private bool _fullscreen;
        private bool _closeLauncherOnGameStart;
        private bool _showGameLogs;
        private bool _soundsEnabled;
        private double _soundsVolume;
        private bool _hasUnsavedChanges;
        private bool _isLoading = true;

        public SettingsViewModel()
        {
            _totalSystemRam = DetectSystemRamMb();
            LoadFromConfig();
        }

        private LauncherSettings Settings => ConfigService.Instance.Data.Settings;

        public void LoadFromConfig()
        {
            _isLoading = true;
            try
            {
                var s = Settings;
                _minRam = s.MinRamMb;
                _maxRam = s.MaxRamMb;
                _javaPath = s.JavaPath ?? "";
                _autoDetectJava = s.AutoDetectJava;
                _jvmArgs = s.JvmArgs;
                _gameDirectory = s.GameDirectory;
                _gameWindowWidth = s.GameWindowWidth;
                _gameWindowHeight = s.GameWindowHeight;
                _fullscreen = s.Fullscreen;
                _closeLauncherOnGameStart = s.CloseLauncherOnGameStart;
                _showGameLogs = s.ShowGameLogs;
                _soundsEnabled = s.SoundsEnabled;
                _soundsVolume = s.SoundsVolume;
                _hasUnsavedChanges = false;

                OnPropertyChanged(string.Empty);
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>Флаг "есть несохранённые изменения" — для показа кнопки Сохранить</summary>
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        private void MarkDirty()
        {
            if (_isLoading) return;
            HasUnsavedChanges = true;
        }

        /// <summary>Мгновенное сохранение (для toggle-переключателей)</summary>
        private void SaveNow()
        {
            if (_isLoading) return;
            _ = ConfigService.Instance.SaveAsync();
        }

        /// <summary>Ручное сохранение (по кнопке)</summary>
        public async Task SaveAllAsync()
        {
            // Применяем текущие значения в Settings перед сохранением
            var s = Settings;
            s.MinRamMb = _minRam;
            s.MaxRamMb = _maxRam;
            s.JavaPath = string.IsNullOrWhiteSpace(_javaPath) ? null : _javaPath;
            s.AutoDetectJava = _autoDetectJava;
            s.JvmArgs = _jvmArgs;
            s.GameDirectory = _gameDirectory;
            s.GameWindowWidth = _gameWindowWidth;
            s.GameWindowHeight = _gameWindowHeight;
            s.Fullscreen = _fullscreen;
            s.CloseLauncherOnGameStart = _closeLauncherOnGameStart;
            s.ShowGameLogs = _showGameLogs;
            s.SoundsEnabled = _soundsEnabled;
            s.SoundsVolume = _soundsVolume;

            await ConfigService.Instance.SaveAsync();

            HasUnsavedChanges = false;
            ToastService.Instance.ShowSuccess("Настройки сохранены");

            Console.WriteLine($"[Settings] Saved manually: MinRam={_minRam}, MaxRam={_maxRam}, Volume={_soundsVolume}");
        }

        // ===== RAM (сохраняется по кнопке) =====
        public int MinRam
        {
            get => _minRam;
            set
            {
                if (_isLoading) return;
                var clamped = Math.Max(512, Math.Min(value, MaxRam));
                if (SetProperty(ref _minRam, clamped))
                {
                    OnPropertyChanged(nameof(MinRamGb));
                    MarkDirty();
                }
            }
        }

        public int MaxRam
        {
            get => _maxRam;
            set
            {
                if (_isLoading) return;
                var upper = Math.Max(TotalSystemRam, 32768);
                var clamped = Math.Max(Math.Max(512, _minRam), Math.Min(value, upper));
                if (SetProperty(ref _maxRam, clamped))
                {
                    OnPropertyChanged(nameof(MaxRamGb));
                    MarkDirty();
                }
            }
        }

        public double MinRamGb => Math.Round(MinRam / 1024.0, 1);
        public double MaxRamGb => Math.Round(MaxRam / 1024.0, 1);

        public int TotalSystemRam
        {
            get => _totalSystemRam;
            set => SetProperty(ref _totalSystemRam, value);
        }

        public double TotalSystemRamGb => Math.Round(TotalSystemRam / 1024.0, 1);
        public int MaxRamSliderMax => Math.Max(TotalSystemRam, 32768);

        // ===== Java (сохраняется по кнопке) =====
        public string JavaPath
        {
            get => _javaPath;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _javaPath, value))
                    MarkDirty();
            }
        }

        // Autodetect — тумблер, сохраняется мгновенно
        public bool AutoDetectJava
        {
            get => _autoDetectJava;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _autoDetectJava, value))
                {
                    Settings.AutoDetectJava = value;
                    if (value) JavaPath = "";
                    SaveNow();
                }
            }
        }

        public string JvmArgs
        {
            get => _jvmArgs;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _jvmArgs, value))
                    MarkDirty();
            }
        }

        // ===== Game window (сохраняется по кнопке) =====
        public int GameWindowWidth
        {
            get => _gameWindowWidth;
            set
            {
                if (_isLoading) return;
                var clamped = Math.Max(640, Math.Min(value, 7680));
                if (SetProperty(ref _gameWindowWidth, clamped))
                    MarkDirty();
            }
        }

        public int GameWindowHeight
        {
            get => _gameWindowHeight;
            set
            {
                if (_isLoading) return;
                var clamped = Math.Max(480, Math.Min(value, 4320));
                if (SetProperty(ref _gameWindowHeight, clamped))
                    MarkDirty();
            }
        }

        // Тумблер полного экрана — сохраняется мгновенно
        public bool Fullscreen
        {
            get => _fullscreen;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _fullscreen, value))
                {
                    Settings.Fullscreen = value;
                    SaveNow();
                }
            }
        }

        // ===== Launcher (сохраняется по кнопке для путей, мгновенно для тумблеров) =====
        public string GameDirectory
        {
            get => _gameDirectory;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _gameDirectory, value))
                    MarkDirty();
            }
        }

        public bool CloseLauncherOnGameStart
        {
            get => _closeLauncherOnGameStart;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _closeLauncherOnGameStart, value))
                {
                    Settings.CloseLauncherOnGameStart = value;
                    SaveNow();
                }
            }
        }

        public bool ShowGameLogs
        {
            get => _showGameLogs;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _showGameLogs, value))
                {
                    Settings.ShowGameLogs = value;
                    SaveNow();
                }
            }
        }

        // ===== Sounds =====
        public bool SoundsEnabled
        {
            get => _soundsEnabled;
            set
            {
                if (_isLoading) return;
                if (SetProperty(ref _soundsEnabled, value))
                {
                    Settings.SoundsEnabled = value;
                    SaveNow();
                }
            }
        }

        public double SoundsVolume
        {
            get => _soundsVolume;
            set
            {
                if (_isLoading) return;
                var clamped = Math.Max(0, Math.Min(1, value));
                if (SetProperty(ref _soundsVolume, clamped))
                {
                    OnPropertyChanged(nameof(SoundsVolumePercent));
                    OnPropertyChanged(nameof(SoundsVolumeSlider));
                    MarkDirty();
                }
            }
        }

        public double SoundsVolumeSlider
        {
            get => _soundsVolume * 100;
            set
            {
                if (_isLoading) return;
                var newVol = value / 100.0;
                if (Math.Abs(_soundsVolume - newVol) > 0.001)
                {
                    SoundsVolume = newVol;
                }
            }
        }

        public int SoundsVolumePercent => (int)Math.Round(_soundsVolume * 100);

        // ===== Info =====
        public string LauncherVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.1";
        public string ConfigFilePath => PathService.ConfigFile;
        public string RootDirectory => PathService.RootDirectory;

        // ===== Actions =====
        public void OpenGameFolder()
        {
            try
            {
                if (Directory.Exists(GameDirectory))
                    Process.Start(new ProcessStartInfo(GameDirectory) { UseShellExecute = true });
                else if (Directory.Exists(PathService.MinecraftDirectory))
                    Process.Start(new ProcessStartInfo(PathService.MinecraftDirectory) { UseShellExecute = true });
            }
            catch { }
        }

        public void OpenLauncherFolder()
        {
            try
            {
                Directory.CreateDirectory(PathService.RootDirectory);
                Process.Start(new ProcessStartInfo(PathService.RootDirectory) { UseShellExecute = true });
            }
            catch { }
        }

        public void OpenLogsFolder()
        {
            try
            {
                Directory.CreateDirectory(PathService.LogsDirectory);
                Process.Start(new ProcessStartInfo(PathService.LogsDirectory) { UseShellExecute = true });
            }
            catch { }
        }

        public void TestSound()
        {
            SoundService.Instance.PlayNotification(ToastType.Info);
        }

        public async Task ResetToDefaultsAsync()
        {
            var defaults = new LauncherSettings
            {
                GameDirectory = PathService.MinecraftDirectory,
                MaxRamMb = Math.Min(TotalSystemRam / 2, 8192)
            };
            var cfg = Settings;
            cfg.MinRamMb = defaults.MinRamMb;
            cfg.MaxRamMb = defaults.MaxRamMb;
            cfg.JavaPath = defaults.JavaPath;
            cfg.AutoDetectJava = defaults.AutoDetectJava;
            cfg.JvmArgs = defaults.JvmArgs;
            cfg.GameDirectory = defaults.GameDirectory;
            cfg.GameWindowWidth = defaults.GameWindowWidth;
            cfg.GameWindowHeight = defaults.GameWindowHeight;
            cfg.Fullscreen = defaults.Fullscreen;
            cfg.CloseLauncherOnGameStart = defaults.CloseLauncherOnGameStart;
            cfg.ShowGameLogs = defaults.ShowGameLogs;
            cfg.SoundsEnabled = defaults.SoundsEnabled;
            cfg.SoundsVolume = defaults.SoundsVolume;

            LoadFromConfig();
            await ConfigService.Instance.SaveAsync();
            ToastService.Instance.ShowInfo("Настройки сброшены");
        }

        private static int DetectSystemRamMb()
        {
            try
            {
                var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                return (int)(totalBytes / (1024L * 1024));
            }
            catch
            {
                return 16384;
            }
        }
    }
}