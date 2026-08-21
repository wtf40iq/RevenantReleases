using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class ToastService
    {
        private static readonly Lazy<ToastService> _instance = new(() => new ToastService());
        public static ToastService Instance => _instance.Value;

        private const int MaxToasts = 5;

        public ObservableCollection<ToastNotification> Toasts { get; } = new();

        private ToastService() { }

        public void Show(string title, string message, ToastType type = ToastType.Info, int durationMs = 4000)
        {
            var toast = new ToastNotification
            {
                Title = title,
                Message = message,
                Type = type,
                DurationMs = durationMs
            };

            Dispatcher.UIThread.Post(() =>
            {
                while (Toasts.Count >= MaxToasts)
                    Toasts.RemoveAt(0);

                Toasts.Add(toast);

                _ = AnimateProgressAsync(toast);
            });

            SoundService.Instance.PlayNotification(type);
        }

        public void ShowSuccess(string title, string message = "") => Show(title, message, ToastType.Success);
        public void ShowError(string title, string message = "") => Show(title, message, ToastType.Error);
        public void ShowWarning(string title, string message = "") => Show(title, message, ToastType.Warning);
        public void ShowInfo(string title, string message = "") => Show(title, message, ToastType.Info);

        public void Remove(ToastNotification toast)
        {
            Dispatcher.UIThread.Post(() =>
            {
                toast.IsVisible = false;
                _ = RemoveDelayedAsync(toast);
            });
        }

        /// <summary>Плавно уменьшает TimeProgress от 100 до 0 за DurationMs</summary>
        private async Task AnimateProgressAsync(ToastNotification toast)
        {
            var startTime = DateTime.UtcNow;

            // Тик ~16мс = 60 обновлений в секунду — полоса идёт плавно
            while (true)
            {
                if (!Toasts.Contains(toast) || !toast.IsVisible) return;

                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed >= toast.DurationMs) break;

                var remaining = Math.Max(0, 100 - (elapsed / toast.DurationMs * 100));
                toast.TimeProgress = remaining;

                await Task.Delay(16);
            }

            if (Toasts.Contains(toast))
                Remove(toast);
        }

        private async Task RemoveDelayedAsync(ToastNotification toast)
        {
            await Task.Delay(350);
            Dispatcher.UIThread.Post(() =>
            {
                if (Toasts.Contains(toast))
                    Toasts.Remove(toast);
            });
        }

        public void ClearAll()
        {
            Dispatcher.UIThread.Post(() => Toasts.Clear());
        }
    }
}