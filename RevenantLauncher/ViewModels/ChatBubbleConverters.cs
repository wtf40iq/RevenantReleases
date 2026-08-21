using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Layout;

namespace RevenantLauncher.ViewModels
{
    public sealed class ChatBubbleBrushConverter : IValueConverter
    {
        public static readonly ChatBubbleBrushConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool mine && mine
                ? new SolidColorBrush(Color.Parse("#604D9CFF"))
                : new SolidColorBrush(Color.Parse("#352A3048"));
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class ChatBubbleAlignmentConverter : IValueConverter
    {
        public static readonly ChatBubbleAlignmentConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool mine && mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
