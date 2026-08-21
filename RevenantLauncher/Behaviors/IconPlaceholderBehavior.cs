using Avalonia;
using Avalonia.Controls;

namespace RevenantLauncher.Behaviors
{
    /// <summary>
    /// Прячет заглушку-соседа по Panel, пока в Image грузится картинка, и показывает её
    /// обратно, когда картинки нет. Без этого заглушка просвечивает сквозь прозрачные
    /// области PNG-иконок модов.
    /// </summary>
    public static class IconPlaceholderBehavior
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Image, bool>(
                "IsEnabled", typeof(IconPlaceholderBehavior));

        public static bool GetIsEnabled(Image c) => c.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Image c, bool value) => c.SetValue(IsEnabledProperty, value);

        static IconPlaceholderBehavior()
        {
            IsEnabledProperty.Changed.AddClassHandler<Image>((image, e) =>
            {
                if ((bool)e.NewValue!)
                {
                    image.PropertyChanged += OnImagePropertyChanged;
                    UpdatePlaceholder(image);
                }
                else
                {
                    image.PropertyChanged -= OnImagePropertyChanged;
                }
            });
        }

        private static void OnImagePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Image.SourceProperty && sender is Image image)
                UpdatePlaceholder(image);
        }

        private static void UpdatePlaceholder(Image image)
        {
            if (image.Parent is not Panel panel) return;

            foreach (var child in panel.Children)
            {
                if (child is Visual visual && !ReferenceEquals(visual, image))
                    visual.IsVisible = image.Source == null;
            }
        }
    }
}
