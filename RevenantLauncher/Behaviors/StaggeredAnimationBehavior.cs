using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace RevenantLauncher.Behaviors
{
    public static class StaggeredAnimationBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "IsEnabled", typeof(StaggeredAnimationBehavior));

        public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

        private static int _globalCounter = 0;
        private static DateTime _lastReset = DateTime.UtcNow;

        // Защита от повторной анимации одного и того же контрола
        private static readonly HashSet<int> _animatedControls = new();

        static StaggeredAnimationBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
            {
                if ((bool)e.NewValue!)
                    AttachAnimation(c);
            });
        }

        private static void AttachAnimation(Control control)
        {
            control.AttachedToVisualTree += async (_, _) =>
            {
                // Каждый контрол анимируется только ОДИН раз
                var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(control);
                if (_animatedControls.Contains(id)) return;
                _animatedControls.Add(id);

                // Сброс счётчика если прошло больше 200мс с последнего элемента
                if ((DateTime.UtcNow - _lastReset).TotalMilliseconds > 200)
                    _globalCounter = 0;
                _lastReset = DateTime.UtcNow;

                int index = _globalCounter++;
                int delayMs = Math.Min(index, 12) * 30; // Меньше задержка + меньше карточек

                control.Opacity = 0;
                var translate = new TranslateTransform(0, 12);
                control.RenderTransform = translate;

                if (delayMs > 0)
                    await Task.Delay(delayMs);

                // Нативные Avalonia-анимации (рендерятся в кадре, без рывков)
                var fade = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(280),
                    Easing = new CubicEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
                    }
                };

                var slide = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(280),
                    Easing = new CubicEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, 12.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0.0) } }
                    }
                };

                try
                {
                    var fadeTask = fade.RunAsync(control);
                    var slideTask = slide.RunAsync(translate);
                    await Task.WhenAll(fadeTask, slideTask);
                }
                catch { }

                control.Opacity = 1;
                control.RenderTransform = null;
            };

            // Очистка при удалении из дерева
            control.DetachedFromVisualTree += (_, _) =>
            {
                var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(control);
                _animatedControls.Remove(id);
            };
        }
    }
}