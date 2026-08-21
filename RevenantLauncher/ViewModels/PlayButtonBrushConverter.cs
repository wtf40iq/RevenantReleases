using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RevenantLauncher.ViewModels
{
    public class PlayButtonBrushConverter : IMultiValueConverter
    {
        public static readonly PlayButtonBrushConverter Instance = new();

        private static readonly IBrush InstalledBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#7C4DFF"), 0),
                new GradientStop(Color.Parse("#536DFE"), 1)
            }
        };

        private static readonly IBrush DownloadBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#4FC3F7"), 0),
                new GradientStop(Color.Parse("#2196F3"), 1)
            }
        };

        private static readonly IBrush RunningBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#5A5A66"), 0),
                new GradientStop(Color.Parse("#3E3E48"), 1)
            }
        };

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // Вторая привязка — IsGameRunning: пока игра запущена, кнопка серая
            if (values.Count > 1 && values[1] is bool running && running)
                return RunningBrush;

            if (values.Count > 0 && values[0] is bool installed)
                return installed ? InstalledBrush : DownloadBrush;
            return InstalledBrush;
        }
    }
}