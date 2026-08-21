using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RevenantLauncher.ViewModels
{
    /// <summary>Конвертирует процент (0-100) в ширину для полоски прогресса тоста</summary>
    public class ToastProgressToWidthConverter : IValueConverter
    {
        public static readonly ToastProgressToWidthConverter Instance = new();

        // Ширина тоста 360, отступы по 0 (Border.ClipToBounds уже режет)
        private const double ToastWidth = 360;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double progress)
                return ToastWidth * (progress / 100.0);
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}