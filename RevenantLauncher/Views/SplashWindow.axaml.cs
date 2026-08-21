using System;
using System.Threading.Tasks;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using RevenantLauncher.Services;

namespace RevenantLauncher.Views
{
    public partial class SplashWindow : Window
    {
        private bool _closing;
        private double _currentProgress;
        private double _targetProgress;

        public SplashWindow()
        {
            InitializeComponent();
            Opened += async (_, _) => await RunEntranceAnimation();
            _ = RunProgressTicker();
        }

        public void UpdateStatus(string text, double? progress = null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var status = this.FindControl<TextBlock>("StatusText");
                if (status != null) status.Text = text;

                // Только поднимаем цель — тикер сам плавно доведёт полоску
                if (progress.HasValue)
                    _targetProgress = Math.Max(_targetProgress, Math.Max(0, Math.Min(1, progress.Value)));
            });
        }

        /// <summary>
        /// Полоска плавно "ползёт" к цели: быстро сразу после нового этапа,
        /// затем замедляется — без рывков и простоя на месте.
        /// </summary>
        private async Task RunProgressTicker()
        {
            const int stepMs = 40;

            while (!_closing)
            {
                var diff = _targetProgress - _currentProgress;

                if (diff > 0.0005)
                {
                    // Финальная доводка до 100% быстрее, чем фоновое ползение
                    var factor = _targetProgress >= 1.0 ? 0.16 : 0.07;
                    var step = Math.Max(0.0012, diff * factor);
                    _currentProgress = Math.Min(_targetProgress, _currentProgress + step);

                    await Dispatcher.UIThread.InvokeAsync(ApplyProgressUi);
                }

                await Task.Delay(stepMs);
            }
        }

        private void ApplyProgressUi()
        {
            var progressFill = this.FindControl<Border>("ProgressFill");
            var progressText = this.FindControl<TextBlock>("ProgressText");
            if (progressFill == null || progressText == null) return;

            const double containerWidth = 520;
            progressFill.Width = containerWidth * _currentProgress;
            progressText.Text = $"{(int)(_currentProgress * 100)}%";
        }

        private async Task RunEntranceAnimation()
        {
            var logoContainer = this.FindControl<Border>("LogoContainer");
            var loadingContainer = this.FindControl<StackPanel>("LoadingContainer");
            var glow = this.FindControl<Ellipse>("GlowPulse");

            if (logoContainer == null || loadingContainer == null || glow == null) return;

            // Начальное состояние
            logoContainer.Opacity = 0;
            logoContainer.RenderTransform = new ScaleTransform(0.7, 0.7);
            loadingContainer.Opacity = 0;
            loadingContainer.RenderTransform = new TranslateTransform(0, 15);
            glow.Opacity = 0;

            // 1. Пульсирующее свечение (постоянное)
            _ = AnimatePulsingGlow(glow);

            // 2. Появление логотипа
            await AnimateLogoEntrance(logoContainer, 600);

            // 3. Появление индикатора загрузки
            await Task.Delay(200);
            _ = AnimateFadeAndSlide(loadingContainer, 0, 1, 15, 0, 350);
        }

        public async Task RunExitAnimation()
        {
            var root = this.FindControl<Border>("RootBorder");
            if (root == null) return;

            // Плавно дожимаем прогресс до 100
            _targetProgress = 1.0;
            var waited = 0;
            while (_currentProgress < 0.995 && waited < 2500)
            {
                await Task.Delay(40);
                waited += 40;
            }

            _currentProgress = 1.0;
            await Dispatcher.UIThread.InvokeAsync(ApplyProgressUi);
            await Task.Delay(150);

            _closing = true;

            var duration = 400;
            var steps = 20;
            var easing = new CubicEaseIn();

            for (int i = 1; i <= steps; i++)
            {
                double progress = (double)i / steps;
                double eased = easing.Ease(progress);

                double opacity = 1.0 - eased;
                double scale = 1.0 - eased * 0.1;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    root.Opacity = opacity;
                    root.RenderTransform = new ScaleTransform(scale, scale);
                });

                await Task.Delay(duration / steps);
            }
        }

        // ===== Animation helpers =====

        private async Task AnimateLogoEntrance(Border logo, int durationMs)
        {
            const int steps = 30;
            var scaleEasing = new BackEaseOut();
            var fadeEasing = new CubicEaseOut();

            double fromScale = 0.7;
            double toScale = 1.0;

            for (int i = 1; i <= steps; i++)
            {
                if (_closing) return;

                double t = (double)i / steps;
                double scaleP = scaleEasing.Ease(t);
                double fadeP = fadeEasing.Ease(Math.Min(1.0, t * 1.5));

                double scale = fromScale + (toScale - fromScale) * scaleP;
                double opacity = fadeP;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    logo.Opacity = opacity;
                    logo.RenderTransform = new ScaleTransform(scale, scale);
                });

                await Task.Delay(durationMs / steps);
            }

            // Гарантируем финальное состояние
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                logo.Opacity = 1;
                logo.RenderTransform = new ScaleTransform(1, 1);
            });
        }

        private static async Task AnimateFadeAndSlide(Control control, double fromOpacity, double toOpacity,
                                                       double fromY, double toY, int durationMs)
        {
            const int steps = 25;
            var easing = new CubicEaseOut();

            for (int i = 1; i <= steps; i++)
            {
                double progress = (double)i / steps;
                double eased = easing.Ease(progress);

                double opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
                double y = fromY + (toY - fromY) * eased;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    control.Opacity = opacity;
                    control.RenderTransform = new TranslateTransform(0, y);
                });

                await Task.Delay(durationMs / steps);
            }
        }

        private async Task AnimatePulsingGlow(Ellipse glow)
        {
            var easing = new SineEaseInOut();
            const int stepDelay = 30;
            const int halfDurationMs = 1400;
            int steps = halfDurationMs / stepDelay;

            while (!_closing)
            {
                for (int i = 1; i <= steps; i++)
                {
                    if (_closing) return;
                    double t = (double)i / steps;
                    double opacity = 0.35 + (0.8 - 0.35) * easing.Ease(t);
                    await Dispatcher.UIThread.InvokeAsync(() => glow.Opacity = opacity);
                    await Task.Delay(stepDelay);
                }
                for (int i = 1; i <= steps; i++)
                {
                    if (_closing) return;
                    double t = (double)i / steps;
                    double opacity = 0.8 - (0.8 - 0.35) * easing.Ease(t);
                    await Dispatcher.UIThread.InvokeAsync(() => glow.Opacity = opacity);
                    await Task.Delay(stepDelay);
                }
            }
        }
    }
}