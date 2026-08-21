using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RevenantLauncher.ViewModels
{
    public class NavColorConverter : IValueConverter
    {
        public static readonly NavColorConverter Instance = new();

        private static readonly IBrush Active = new SolidColorBrush(Color.Parse("#B388FF"));
        private static readonly IBrush Inactive = new SolidColorBrush(Color.Parse("#80FFFFFF"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? Active : Inactive;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}