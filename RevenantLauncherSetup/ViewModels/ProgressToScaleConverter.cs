using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RevenantLauncherSetup.ViewModels
{
    /// <summary>Прогресс (0..100) в масштаб заливки (0..1) — заливка тянется на всю
    /// дорожку и срезается слева направо, градиент не сжимается.</summary>
    public class ProgressToScaleConverter : IValueConverter
    {
        public static readonly ProgressToScaleConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double progress)
                return Math.Max(0, Math.Min(1, progress / 100.0));
            return 0.0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
