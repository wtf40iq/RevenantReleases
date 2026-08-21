using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RevenantLauncher.Services;

namespace RevenantLauncher.Behaviors
{
    /// <summary>
    /// Слой «жидкого стекла» для шаблонов кнопок: рисует под кнопкой размытый срез
    /// баннера, находящийся ровно позади неё в окне — настоящее backdrop-размытие.
    /// В теме Standart ничего не рисует.
    /// </summary>
    public class LiquidGlassLayer : Control
    {
        private static Visual? _host;
        private static IImage? _blurredBanner;

        public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
            AvaloniaProperty.Register<LiquidGlassLayer, CornerRadius>(nameof(CornerRadius));

        public CornerRadius CornerRadius
        {
            get => GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        /// <summary>Элемент, занимающий всю область окна с баннером (для координат среза)</summary>
        public static Visual? Host
        {
            get => _host;
            set => _host = value;
        }

        public LiquidGlassLayer()
        {
            ThemeService.ThemeChanged += OnThemeChanged;
            AssetImageService.BannerChanged += OnBannerChanged;
        }

        private void OnBannerChanged()
        {
            _blurredBanner = null;
            InvalidateVisual();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty)
                InvalidateVisual();
        }

        private void OnThemeChanged(LauncherTheme theme) => InvalidateVisual();

        public override void Render(DrawingContext context)
        {
            if (ThemeService.Current != LauncherTheme.New) return;

            var blur = GetBlurredBanner();
            if (blur == null || _host == null) return;

            var size = Bounds.Size;
            if (size.Width < 2 || size.Height < 2) return;

            var p = this.TranslatePoint(new Point(0, 0), _host);
            var hostSize = _host.Bounds.Size;
            if (p == null || hostSize.Width < 2 || hostSize.Height < 2) return;

            // Срез размытой картинки ровно под этим элементом
            var kx = blur.Size.Width / hostSize.Width;
            var ky = blur.Size.Height / hostSize.Height;
            var src = new Rect(p.Value.X * kx, p.Value.Y * ky, size.Width * kx, size.Height * ky);

            using (context.PushGeometryClip(RoundedRect(new Rect(0, 0, size.Width, size.Height), CornerRadius)))
            {
                // Подложка цвета окна + размытый баннер с той же прозрачностью, что в окне
                context.FillRectangle(new SolidColorBrush(Color.Parse("#1A1025")),
                    new Rect(0, 0, size.Width, size.Height));

                using (context.PushOpacity(0.6))
                {
                    context.DrawImage(blur, src, new Rect(0, 0, size.Width, size.Height));
                }

                // Затемняющий оверлей темы, чтобы стекло сливалось с окружением
                var app = Application.Current;
                if (app != null
                    && app.Resources.TryGetResource("OverlayGradient", app.ActualThemeVariant, out var overlay)
                    && overlay is IBrush brush)
                {
                    context.FillRectangle(brush, new Rect(0, 0, size.Width, size.Height));
                }
            }
        }

        /// <summary>Лениво строит размытую копию баннера: берёт предмасштабированный баннер + сильное уменьшение</summary>
        private static IImage? GetBlurredBanner()
        {
            if (_blurredBanner != null) return _blurredBanner;

            try
            {
                var banner = AssetImageService.Instance.CurrentBanner;
                if (banner == null) return null;

                // сильное уменьшение — при растяении даёт мягкое размытие
                var blur = new RenderTargetBitmap(new PixelSize(160, 100));
                blur.Render(new Image
                {
                    Source = banner,
                    Width = 160,
                    Height = 100,
                    Stretch = Stretch.Fill
                });

                _blurredBanner = blur;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LiquidGlass] blur init failed: " + ex.Message);
            }

            return _blurredBanner;
        }

        private static StreamGeometry RoundedRect(Rect r, CornerRadius c)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var tl = c.TopLeft; var tr = c.TopRight; var br = c.BottomRight; var bl = c.BottomLeft;

                ctx.BeginFigure(new Point(r.X + tl, r.Y), false);
                LineOrArc(ctx, new Point(r.Right - tr, r.Y), new Point(r.Right, r.Y + tr), tr);
                LineOrArc(ctx, new Point(r.Right, r.Bottom - br), new Point(r.Right - br, r.Bottom), br);
                LineOrArc(ctx, new Point(r.X + bl, r.Bottom), new Point(r.X, r.Bottom - bl), bl);
                LineOrArc(ctx, new Point(r.X, r.Y + tl), new Point(r.X + tl, r.Y), tl);
                ctx.EndFigure(true);
            }
            return g;
        }

        private static void LineOrArc(StreamGeometryContext ctx, Point lineTo, Point arcTo, double radius)
        {
            ctx.LineTo(lineTo);
            if (radius > 0.5)
                ctx.ArcTo(arcTo, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            else
                ctx.LineTo(arcTo);
        }
    }
}
