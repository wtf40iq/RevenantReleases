using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RevenantLauncher.Models;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class ToastContainer : UserControl
    {
        private ToastContainerViewModel ViewModel => (ToastContainerViewModel)DataContext!;

        private readonly HashSet<Border> _animated = new();

        public ToastContainer()
        {
            InitializeComponent();
            AttachedToVisualTree += (_, _) => StartAnimationWatcher();
        }

        private void StartAnimationWatcher()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => AnimateVisibleToasts();
            timer.Start();
        }

        private void AnimateVisibleToasts()
        {
            foreach (var item in this.GetVisualDescendants())
            {
                if (item is Border border && border.Tag is ToastNotification toast)
                {
                    if (!_animated.Contains(border))
                    {
                        _animated.Add(border);
                        Dispatcher.UIThread.Post(() =>
                        {
                            border.Opacity = 1;
                            border.RenderTransform = TransformOperations.Parse("translateX(0px)");
                        }, DispatcherPriority.Render);
                    }

                    if (!toast.IsVisible && border.Opacity > 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            border.Opacity = 0;
                            border.RenderTransform = TransformOperations.Parse("translateX(400px)");
                        }, DispatcherPriority.Render);
                    }
                }
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ToastNotification toast)
            {
                Services.ToastService.Instance.Remove(toast);
            }
        }
    }
}