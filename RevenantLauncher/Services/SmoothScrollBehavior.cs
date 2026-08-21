using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace RevenantLauncher.Services
{
    /// <summary>
    /// Плавный скролл через рендер-хук Avalonia (синхронизирован с частотой монитора).
    /// </summary>
    public static class SmoothScrollBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
                "IsEnabled", typeof(SmoothScrollBehavior));

        public static bool GetIsEnabled(ScrollViewer sv) => sv.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(ScrollViewer sv, bool value) => sv.SetValue(IsEnabledProperty, value);

        private class AnimState
        {
            public double StartY;
            public double TargetY;
            public long StartTicks;
            public bool Active;
        }

        private static readonly Dictionary<ScrollViewer, AnimState> _states = new();
        private static readonly CubicEaseOut _easing = new();
        private const int DurationMs = 300;
        private const double StepPixels = 120;

        static SmoothScrollBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
            {
                if ((bool)e.NewValue!)
                    Attach(sv);
                else
                    Detach(sv);
            });
        }

        private static void Attach(ScrollViewer sv)
        {
            sv.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        }

        private static void Detach(ScrollViewer sv)
        {
            sv.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            _states.Remove(sv);
        }

        private static void OnWheel(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.Extent.Height <= sv.Viewport.Height) return;

            e.Handled = true;

            double currentTarget = _states.TryGetValue(sv, out var existing) && existing.Active
                ? existing.TargetY
                : sv.Offset.Y;

            double step = -e.Delta.Y * StepPixels;
            double maxOffset = sv.Extent.Height - sv.Viewport.Height;
            double newTarget = Math.Max(0, Math.Min(maxOffset, currentTarget + step));

            var state = new AnimState
            {
                StartY = sv.Offset.Y,
                TargetY = newTarget,
                StartTicks = DateTime.UtcNow.Ticks,
                Active = true
            };
            _states[sv] = state;

            RequestFrame(sv);
        }

        private static void RequestFrame(ScrollViewer sv)
        {
            var topLevel = TopLevel.GetTopLevel(sv);
            if (topLevel == null) return;

            topLevel.RequestAnimationFrame(_ => AnimateFrame(sv));
        }

        private static void AnimateFrame(ScrollViewer sv)
        {
            if (!_states.TryGetValue(sv, out var state) || !state.Active) return;

            double elapsedMs = (DateTime.UtcNow.Ticks - state.StartTicks) / (double)TimeSpan.TicksPerMillisecond;
            double progress = Math.Min(1.0, elapsedMs / DurationMs);
            double eased = _easing.Ease(progress);
            double newY = state.StartY + (state.TargetY - state.StartY) * eased;

            sv.Offset = new Vector(sv.Offset.X, newY);

            if (progress >= 1.0)
            {
                state.Active = false;
                _states.Remove(sv);
                return;
            }

            // Запрашиваем следующий кадр (синхронизирован с монитором = 60/120/165 FPS)
            RequestFrame(sv);
        }
    }
}