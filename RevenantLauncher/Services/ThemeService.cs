using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace RevenantLauncher.Services
{
    public enum LauncherTheme
    {
        Standart,
        New
    }

    public enum SidebarStyle
    {
        Gradient,
        Dark
    }

    /// <summary>
    /// Переключение тем оформления. Тема — это набор ресурсов (кисти, радиусы углов),
    /// которые записываются в Application.Current.Resources. Весь темизируемый XAML
    /// ссылается на них через DynamicResource, поэтому смена темы применяется мгновенно.
    /// </summary>
    public static class ThemeService
    {
        public static LauncherTheme Current { get; private set; } = LauncherTheme.Standart;
        public static SidebarStyle CurrentSidebar { get; private set; } = SidebarStyle.Gradient;

        public static event Action<LauncherTheme>? ThemeChanged;

        /// <summary>Применяет тему из сохранённых настроек при старте (до создания MainWindow)</summary>
        public static void InitializeFromConfig()
        {
            var theme = ConfigService.Instance.Data.Settings.Theme is "New" or "Glass"
                ? LauncherTheme.New
                : LauncherTheme.Standart;
            CurrentSidebar = ConfigService.Instance.Data.Settings.SidebarStyle == "Dark"
                ? SidebarStyle.Dark
                : SidebarStyle.Gradient;
            Apply(theme, save: false);
        }

        /// <summary>Применяет тему и (опционально) сохраняет выбор в настройках</summary>
        public static void Apply(LauncherTheme theme, bool save = true)
        {
            Current = theme;

            var res = Application.Current?.Resources;
            if (res != null)
            {
                foreach (var kv in GetValues(theme))
                    res[kv.Key] = kv.Value;

                foreach (var kv in GetSidebarOverrides(theme))
                    res[kv.Key] = kv.Value;
            }

            if (save)
            {
                ConfigService.Instance.Data.Settings.Theme =
                    theme == LauncherTheme.New ? "Glass" : "Standart";
                _ = ConfigService.Instance.SaveAsync();
            }

            ThemeChanged?.Invoke(theme);
        }

        private static Dictionary<string, object> GetValues(LauncherTheme theme)
        {
            return theme == LauncherTheme.New ? GetGlassValues() : GetStandartValues();
        }

        /// <summary>Вариант сайдбара: прозрачный градиент или тёмная панель</summary>
        private static Dictionary<string, object> GetSidebarOverrides(LauncherTheme theme)
        {
            if (CurrentSidebar == SidebarStyle.Dark)
            {
                return new Dictionary<string, object>
                {
                    ["SidebarGradient"] = Brush(theme == LauncherTheme.New ? "#EA140F1E" : "#F5150D20"),
                    ["SidebarBorderBrush"] = Brush("#25FFFFFF")
                };
            }

            return new Dictionary<string, object>
            {
                ["SidebarBorderBrush"] = Brush("#00FFFFFF")
            };
        }

        /// <summary>Переключает вариант сайдбара и сохраняет в настройки</summary>
        public static void SetSidebarStyle(SidebarStyle style, bool save = true)
        {
            if (CurrentSidebar == style) return;
            CurrentSidebar = style;
            Apply(Current, save: false);

            if (save)
            {
                ConfigService.Instance.Data.Settings.SidebarStyle =
                    style == SidebarStyle.Dark ? "Dark" : "Gradient";
                _ = ConfigService.Instance.SaveAsync();
            }
        }

        // ===== Standart: полностью повторяет текущий вид лаунчера =====
        private static Dictionary<string, object> GetStandartValues()
        {
            return new Dictionary<string, object>
            {
                ["WindowBackground"] = Brush("#1A1025"),
                ["WindowBorderBrush"] = Brush("#00FFFFFF"),
                ["BannerOpacity"] = 0.6,
                ["OverlayGradient"] = Gradient(0, 0, 1, 1,
                    ("#90100818", 0), ("#40100818", 0.5), ("#70100818", 1)),
                ["SidebarGradient"] = Gradient(0, 0.5, 1, 0.5,
                    ("#CC0E0A18", 0), ("#000E0A18", 1)),
                ["BottomGradient"] = Gradient(0.5, 1, 0.5, 0,
                    ("#DD0E0A18", 0), ("#000E0A18", 1)),
                ["DialogBackground"] = Brush("#1A1025"),
                ["DialogBorderBrush"] = Brush("#40FFFFFF"),
                ["NavActiveBorderBrush"] = Brush("#00FFFFFF"),
                ["AccountSelectedBackground"] = Brush("#20FFFFFF"),
                ["CardBackground"] = Brush("#181225"),
                ["CardHoverBackground"] = Brush("#221830"),
                ["CardBorderBrush"] = Brush("#25FFFFFF"),
                ["CardCornerRadius"] = new CornerRadius(12),
                ["SectionCornerRadius"] = new CornerRadius(16),
                ["ControlBackground"] = Brush("#40000000"),
                ["ControlBorderBrush"] = Brush("#40FFFFFF"),
                ["ButtonBorderBrush"] = Brush("#50FFFFFF"),
                ["RoundButtonBackground"] = Brush("#40FFFFFF"),
                ["TabBackground"] = Brush("#30FFFFFF"),
                ["TabActiveBackground"] = Brush("#7C4DFF"),
                ["TabActiveHoverBackground"] = Brush("#8B5CFF"),
                ["NavActiveBackground"] = Brush("#2A2040"),
                ["PanelBackground"] = Brush("#E0201828"),
                ["QuickLaunchBackground"] = Brush("#B0181225"),
                ["ToastBackground"] = Brush("#1A1025"),
                ["VersionSelectedBackground"] = Brush("#252035")
            };
        }

        // ===== New: liquid glass «как во второй раз» — окно плотное, как в Standart,
        // а кнопки/вкладки/карточки/диалоги — настоящее жидкое стекло: прозрачная
        // белая тонировка поверх размытого среза баннера (LiquidGlassLayer) =====
        private static Dictionary<string, object> GetGlassValues()
        {
            return new Dictionary<string, object>
            {
                // Окно и фон — без прозрачности, один в один как Standart
                ["WindowBackground"] = Brush("#1A1025"),
                ["WindowBorderBrush"] = Brush("#00FFFFFF"),
                ["BannerOpacity"] = 0.6,
                ["OverlayGradient"] = Gradient(0, 0, 1, 1,
                    ("#90100818", 0), ("#40100818", 0.5), ("#70100818", 1)),
                ["SidebarGradient"] = Gradient(0, 0.5, 1, 0.5,
                    ("#CC0E0A18", 0), ("#000E0A18", 1)),
                ["BottomGradient"] = Gradient(0.5, 1, 0.5, 0,
                    ("#DD0E0A18", 0), ("#000E0A18", 1)),
                // Диалоги — матовое тёмное стекло со светлой кромкой
                ["DialogBackground"] = Gradient(0, 0, 0, 1,
                    ("#E61A1428", 0), ("#D9120D1C", 1)),
                ["DialogBorderBrush"] = Brush("#45FFFFFF"),
                ["NavActiveBorderBrush"] = Brush("#50FFFFFF"),
                ["AccountSelectedBackground"] = Brush("#30FFFFFF"),
                // Панели — белая полупрозрачная тонировка поверх размытия
                ["PanelBackground"] = Gradient(0, 0, 0, 1,
                    ("#26FFFFFF", 0), ("#10FFFFFF", 1)),
                ["QuickLaunchBackground"] = Gradient(0, 0, 0, 1,
                    ("#22FFFFFF", 0), ("#0CFFFFFF", 1)),
                // Карточки
                ["CardBackground"] = Gradient(0, 0, 0, 1,
                    ("#1FFFFFFF", 0), ("#0AFFFFFF", 1)),
                ["CardHoverBackground"] = Gradient(0, 0, 0, 1,
                    ("#2FFFFFFF", 0), ("#16FFFFFF", 1)),
                ["CardBorderBrush"] = Brush("#45FFFFFF"),
                ["CardCornerRadius"] = new CornerRadius(20),
                ["SectionCornerRadius"] = new CornerRadius(24),
                // Кнопки-пилюли, поиск, кнопки окна
                ["ControlBackground"] = Gradient(0, 0, 0, 1,
                    ("#26FFFFFF", 0), ("#0EFFFFFF", 1)),
                ["ControlBorderBrush"] = Brush("#45FFFFFF"),
                ["ButtonBorderBrush"] = Brush("#50FFFFFF"),
                ["RoundButtonBackground"] = Gradient(0, 0, 0, 1,
                    ("#2AFFFFFF", 0), ("#12FFFFFF", 1)),
                // Вкладки и активная навигация — белое стекло
                ["TabBackground"] = Gradient(0, 0, 0, 1,
                    ("#22FFFFFF", 0), ("#0CFFFFFF", 1)),
                ["TabActiveBackground"] = Gradient(0, 0, 0, 1,
                    ("#40FFFFFF", 0), ("#26FFFFFF", 1)),
                ["TabActiveHoverBackground"] = Gradient(0, 0, 0, 1,
                    ("#50FFFFFF", 0), ("#33FFFFFF", 1)),
                ["NavActiveBackground"] = Gradient(0, 0, 0, 1,
                    ("#38FFFFFF", 0), ("#1EFFFFFF", 1)),
                // Тосты — прозрачные, стекло рисует LiquidGlassLayer
                ["ToastBackground"] = Brush("#00000000"),
                ["VersionSelectedBackground"] = Gradient(0, 0, 0, 1,
                    ("#38FFFFFF", 0), ("#20FFFFFF", 1))
            };
        }

        private static SolidColorBrush Brush(string hex) =>
            new SolidColorBrush(Color.Parse(hex));

        private static LinearGradientBrush Gradient(
            double x1, double y1, double x2, double y2,
            params (string color, double offset)[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(x2, y2, RelativeUnit.Relative)
            };
            foreach (var (color, offset) in stops)
                brush.GradientStops.Add(new GradientStop(Color.Parse(color), offset));
            return brush;
        }
    }
}
