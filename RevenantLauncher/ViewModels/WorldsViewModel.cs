using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum WorldsTab
    {
        Worlds,
        Backups,
        Screenshots
    }

    public class WorldsViewModel : ViewModelBase
    {
        private WorldsTab _currentTab = WorldsTab.Worlds;
        private bool _isLoading;
        private bool _isBusy;
        private bool _isFirstLoad = true;
        private bool _isLightboxOpen;
        private ScreenshotInfo? _selectedScreenshot;

        public ObservableCollection<WorldInfo> Worlds { get; } = new();
        public ObservableCollection<BackupInfo> Backups { get; } = new();
        public ObservableCollection<ScreenshotInfo> Screenshots { get; } = new();

        public ConfirmDialogViewModel ConfirmDialog { get; } = new();

        private bool _isConfirmOpen;
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            set => SetProperty(ref _isConfirmOpen, value);
        }

        public WorldsViewModel()
        {
            ConfirmDialog.DialogClosed += () => IsConfirmOpen = false;
        }

        private Task<bool> AskAsync(string title, string message, string yes, string no)
        {
            IsConfirmOpen = true;
            return ConfirmDialog.ShowAsync(title, message, yes, no);
        }

        public WorldsTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsWorldsTab));
                    OnPropertyChanged(nameof(IsBackupsTab));
                    OnPropertyChanged(nameof(IsScreenshotsTab));
                    _ = RefreshAsync();
                }
            }
        }

        public bool IsWorldsTab => CurrentTab == WorldsTab.Worlds;
        public bool IsBackupsTab => CurrentTab == WorldsTab.Backups;
        public bool IsScreenshotsTab => CurrentTab == WorldsTab.Screenshots;

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        public string StatsDisplay =>
            $"{Worlds.Count} миров · {Backups.Count} бэкапов · {Screenshots.Count} скриншотов";

        // ===== Лайтбокс скриншотов =====

        public bool IsLightboxOpen
        {
            get => _isLightboxOpen;
            set => SetProperty(ref _isLightboxOpen, value);
        }

        public ScreenshotInfo? SelectedScreenshot
        {
            get => _selectedScreenshot;
            set => SetProperty(ref _selectedScreenshot, value);
        }

        private Avalonia.Media.Imaging.Bitmap? _lightboxImage;
        public Avalonia.Media.Imaging.Bitmap? LightboxImage
        {
            get => _lightboxImage;
            set => SetProperty(ref _lightboxImage, value);
        }

        public void SetTab(WorldsTab tab) => CurrentTab = tab;

        public async Task InitializeAsync()
        {
            if (!_isFirstLoad) return;
            _isFirstLoad = false;
            await RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            IsLoading = true;
            try
            {
                var worldsTask = WorldService.Instance.LoadWorldsAsync();
                var backupsTask = WorldService.Instance.LoadBackupsAsync();
                var shotsTask = CurrentTab == WorldsTab.Screenshots
                    ? WorldService.Instance.LoadScreenshotsAsync()
                    : Task.FromResult(new System.Collections.Generic.List<ScreenshotInfo>());

                await Task.WhenAll(worldsTask, backupsTask, shotsTask);

                var worlds = await worldsTask;
                var backups = await backupsTask;
                var shots = await shotsTask;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Worlds.Clear();
                    foreach (var w in worlds) Worlds.Add(w);

                    Backups.Clear();
                    foreach (var b in backups) Backups.Add(b);

                    if (CurrentTab == WorldsTab.Screenshots)
                    {
                        Screenshots.Clear();
                        foreach (var s in shots) Screenshots.Add(s);
                    }

                    OnPropertyChanged(nameof(StatsDisplay));
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ===== Миры =====

        public async Task BackupWorldAsync(WorldInfo world)
        {
            IsBusy = true;
            try
            {
                var backup = await WorldService.Instance.CreateBackupAsync(world);
                ToastService.Instance.ShowSuccess("Бэкап создан", $"{world.Name} · {backup.SizeDisplay}");
                await RefreshAsync();
            }
            catch (System.IO.IOException)
            {
                ToastService.Instance.ShowError("Не удалось создать бэкап",
                    "Мир сейчас запущен — игра заблокировала файлы. Завершите игру и попробуйте снова.");
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка бэкапа", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DeleteWorldAsync(WorldInfo world)
        {
            var ok = await AskAsync(
                "Удалить мир",
                $"Мир «{world.Name}» будет удалён безвозвратно вместе со всеми постройками. Продолжить?",
                "Удалить", "Отмена");
            if (!ok) return;

            await WorldService.Instance.DeleteWorldAsync(world);
            ToastService.Instance.ShowInfo("Мир удалён", world.Name);
            await RefreshAsync();
        }

        public void OpenWorldFolder(WorldInfo world) => WorldService.Instance.OpenWorldFolder(world);

        // ===== Бэкапы =====

        public async Task RestoreBackupAsync(BackupInfo backup)
        {
            var ok = await AskAsync(
                "Восстановить мир",
                $"Текущий мир «{backup.WorldName}» будет заменён копией из бэкапа от {backup.CreatedDisplay}. Продолжить?",
                "Восстановить", "Отмена");
            if (!ok) return;

            IsBusy = true;
            try
            {
                await WorldService.Instance.RestoreBackupAsync(backup);
                ToastService.Instance.ShowSuccess("Мир восстановлен", backup.WorldName);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError("Ошибка восстановления", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task DeleteBackupAsync(BackupInfo backup)
        {
            var ok = await AskAsync(
                "Удалить бэкап",
                $"Бэкап мира «{backup.WorldName}» от {backup.CreatedDisplay} будет удалён.",
                "Удалить", "Отмена");
            if (!ok) return;

            await WorldService.Instance.DeleteBackupAsync(backup);
            ToastService.Instance.ShowInfo("Бэкап удалён", backup.WorldName);
            await RefreshAsync();
        }

        public void OpenBackupsFolder() => WorldService.Instance.OpenBackupsFolder();

        // ===== Скриншоты =====

        public void OpenLightbox(ScreenshotInfo shot)
        {
            SelectedScreenshot = shot;
            LoadLightboxImage(shot);
            IsLightboxOpen = true;
        }

        public void CloseLightbox()
        {
            IsLightboxOpen = false;
            LightboxImage = null;
        }

        public void NextScreenshot() => StepScreenshot(1);
        public void PrevScreenshot() => StepScreenshot(-1);

        private void StepScreenshot(int dir)
        {
            if (SelectedScreenshot == null || Screenshots.Count == 0) return;
            var idx = Screenshots.IndexOf(SelectedScreenshot);
            if (idx < 0) return;
            idx = (idx + dir + Screenshots.Count) % Screenshots.Count;
            SelectedScreenshot = Screenshots[idx];
            LoadLightboxImage(SelectedScreenshot);
        }

        private void LoadLightboxImage(ScreenshotInfo shot)
        {
            try
            {
                using var fs = System.IO.File.OpenRead(shot.FilePath);
                LightboxImage = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(
                    fs, 1600, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            }
            catch
            {
                LightboxImage = null;
            }
        }

        public void OpenScreenshotFile()
        {
            if (SelectedScreenshot != null)
                WorldService.Instance.OpenFile(SelectedScreenshot.FilePath);
        }

        public async Task DeleteScreenshotAsync(ScreenshotInfo shot)
        {
            await WorldService.Instance.DeleteScreenshotAsync(shot);
            Screenshots.Remove(shot);
            if (SelectedScreenshot == shot)
                CloseLightbox();
            OnPropertyChanged(nameof(StatsDisplay));
            ToastService.Instance.ShowInfo("Скриншот удалён", shot.FileName);
        }

        public void OpenScreenshotsFolder() => WorldService.Instance.OpenScreenshotsFolder();
    }
}
