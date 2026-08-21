using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RevenantLauncherSetup.ViewModels
{
    public class ProgressToWidthConverter : IValueConverter
    {
        public static readonly ProgressToWidthConverter Instance = new();

        // Ширина прогресс-бара в LocationPage — MaxWidth="380"
        private const double BarWidth = 380;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double progress)
                return BarWidth * (progress / 100.0);
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}