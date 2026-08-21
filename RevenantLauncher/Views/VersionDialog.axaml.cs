using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using RevenantLauncher.Models;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class VersionDialog : UserControl
    {
        private VersionDialogViewModel ViewModel => (VersionDialogViewModel)DataContext!;

        public VersionDialog()
        {
            InitializeComponent();

            // Анимация при каждом открытии диалога
            this.GetObservable(Visual.IsVisibleProperty).Subscribe(async visible =>
            {
                if (visible) await PlayOpenAnimationAsync();
            });
        }

        /// <summary>Плавное появление панели (фон затемняется мгновенно)</summary>
        private async Task PlayOpenAnimationAsync()
        {
            try
            {
                Backdrop.Opacity = 1;
                DialogPanel.Opacity = 0;
                DialogPanel.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                var scale = new ScaleTransform(0.94, 0.94);
                DialogPanel.RenderTransform = scale;

                var panelFade = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = new CubicEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
                    }
                };

                var panelScale = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(240),
                    Easing = new CubicEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 0.94),
                            new Setter(ScaleTransform.ScaleYProperty, 0.94)
                        } },
                        new KeyFrame { Cue = new Cue(1), Setters =
                        {
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0)
                        } }
                    }
                };

                var tasks = new List<Task>
                {
                    panelFade.RunAsync(DialogPanel),
                    panelScale.RunAsync(scale)
                };
                await Task.WhenAll(tasks);

                Backdrop.Opacity = 1;
                DialogPanel.Opacity = 1;
                DialogPanel.RenderTransform = null;
            }
            catch
            {
                // анимация не критична — просто показываем как есть
                Backdrop.Opacity = 1;
                DialogPanel.Opacity = 1;
                DialogPanel.RenderTransform = null;
            }
        }

        private void TabVanilla_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(VersionTab.Vanilla);
        private void TabForge_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(VersionTab.Forge);
        private void TabFabric_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(VersionTab.Fabric);

        private void ChipInstalled_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleOnlyInstalled();
        private void ChipReleases_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleReleases();
        private void ChipSnapshots_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleSnapshots();
        private void ChipOld_Click(object? sender, RoutedEventArgs e) => ViewModel.ToggleOld();

        private void Close_Click(object? sender, RoutedEventArgs e) => ViewModel.Cancel();

        private void VersionItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GameVersion version)
                ViewModel.OnMcVersionClicked(version);
        }

        private void LoaderItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LoaderVersionItem item)
                ViewModel.OnLoaderVersionClicked(item);
        }

        private void UseLatest_Click(object? sender, RoutedEventArgs e) => ViewModel.UseLatestLoader();

        private void BackToMc_Click(object? sender, RoutedEventArgs e) => ViewModel.BackToMcList();
    }
}
