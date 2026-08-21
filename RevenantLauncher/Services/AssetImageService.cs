using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RevenantLauncher.Services
{
    /// <summary>
    /// Хранит предварительно уменьшенные копии крупных ассетов.
    /// Без этого каждый кадр рендера масштабирует 4K-баннер и 5000px логотипы —
    /// главная причина лагов на главной странице. Визуально результат идентичен:
    /// окна всё равно показывают эти картинки в размере <= 1280px.
    /// </summary>
    public class AssetImageService
    {
        private static readonly Lazy<AssetImageService> _instance = new(() => new AssetImageService());
        public static AssetImageService Instance => _instance.Value;

        private Bitmap? _launcherLogo;
        private Bitmap? _fonLogo;
        private Bitmap? _logo;

        /// <summary>Доступные фоны лаунчера (Assets/mainbanner)</summary>
        public static readonly string[] AvailableBanners =
        {
            "banner1.jpg", "banner2.jpg", "banner3.jpg", "banner4.jpg", "banner5.jpg"
        };

        public static event Action? BannerChanged;

        private static string _currentBannerName = "";
        private static readonly System.Collections.Generic.Dictionary<string, Bitmap?> _bannerCache = new();
        private static readonly System.Collections.Generic.Dictionary<string, Bitmap?> _previewCache = new();

        /// <summary>Имя выбранного фона (по умолчанию banner1.jpg)</summary>
        public static string CurrentBannerName =>
            string.IsNullOrEmpty(_currentBannerName) ? "banner1.jpg" : _currentBannerName;

        /// <summary>Красивое название фона для меню кастомизации</summary>
        public static string GetBannerDisplayName(string name) => name switch
        {
            "banner1.jpg" => "Классика",
            "banner2.jpg" => "Закат и кот",
            "banner3.jpg" => "Вишнёвая долина",
            "banner4.jpg" => "Северное сияние",
            "banner5.jpg" => "Королевство",
            _ => name
        };

        /// <summary>Выбирает фон и сохраняет в настройки</summary>
        public static void SelectBanner(string name)
        {
            if (_currentBannerName == name) return;
            _currentBannerName = name;

            ConfigService.Instance.Data.Settings.Banner = name;
            _ = ConfigService.Instance.SaveAsync();

            BannerChanged?.Invoke();
        }

        /// <summary>Подтягивает сохранённый фон после загрузки конфига (без повторного сохранения)</summary>
        public static void ApplyBannerFromConfig()
        {
            var name = ConfigService.Instance.Data.Settings.Banner;
            if (string.IsNullOrEmpty(name) || name == _currentBannerName) return;

            _currentBannerName = name;
            BannerChanged?.Invoke();
        }

        /// <summary>Текущий фон, уменьшенный под окно (исходники до 4K)</summary>
        public Bitmap? CurrentBanner => GetBanner(CurrentBannerName);

        public Bitmap? GetBanner(string name)
        {
            lock (_bannerCache)
            {
                if (_bannerCache.TryGetValue(name, out var cached))
                    return cached;
            }

            var bmp = LoadScaled($"mainbanner/{name}", 1280);
            lock (_bannerCache) _bannerCache[name] = bmp;
            return bmp;
        }

        /// <summary>Маленькое превью для карточек выбора фона</summary>
        public Bitmap? GetBannerPreview(string name)
        {
            lock (_previewCache)
            {
                if (_previewCache.TryGetValue(name, out var cached))
                    return cached;
            }

            var bmp = LoadScaled($"mainbanner/{name}", 320);
            lock (_previewCache) _previewCache[name] = bmp;
            return bmp;
        }

        /// <summary>Большой логотип главной страницы (исходник 5000x2000)</summary>
        public Bitmap? LauncherLogo => _launcherLogo ??= LoadScaled("launcher.png", 1000);

        /// <summary>Логотип сайдбара (исходник 2000x2000, показывается в 72px)</summary>
        public Bitmap? FonLogo => _fonLogo ??= LoadScaled("fonlogo.png", 144);

        /// <summary>Маленький логотип для списков (исходник 2000x2000, показывается в 32px)</summary>
        public Bitmap? Logo => _logo ??= LoadScaled("Rlogo.png", 96);

        private static readonly System.Collections.Generic.Dictionary<string, Bitmap> _smallIcons = new();

        /// <summary>Кешированная иконка-ассет (vanilla.png / forge.png / fabric.png)</summary>
        public static Bitmap? GetIcon(string assetName)
        {
            lock (_smallIcons)
            {
                if (_smallIcons.TryGetValue(assetName, out var cached))
                    return cached;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://RevenantLauncher/Assets/" + assetName));
                var bmp = new Bitmap(stream);
                lock (_smallIcons) _smallIcons[assetName] = bmp;
                return bmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assets] failed to load icon {assetName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Прогрев всех ассетов разом (вызывается один раз на сплэше)</summary>
        public void Initialize()
        {
            try
            {
                _currentBannerName = ConfigService.Instance.Data.Settings.Banner;
                _ = CurrentBanner;
                _ = LauncherLogo;
                _ = FonLogo;
                _ = Logo;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Assets] prescale failed: " + ex.Message);
            }
        }

        /// <summary>Уменьшает картинку при декодировании (быстро, без RenderTargetBitmap)</summary>
        private static Bitmap? LoadScaled(string asset, int targetWidth)
        {
            var uri = new Uri("avares://RevenantLauncher/Assets/" + asset);
            try
            {
                // Мелкие картинки не трогаем
                using (var probeStream = AssetLoader.Open(uri))
                {
                    var probe = new Bitmap(probeStream);
                    if (probe.PixelSize.Width <= targetWidth)
                        return probe;
                    probe.Dispose();
                }

                using var stream = AssetLoader.Open(uri);
                return Bitmap.DecodeToWidth(stream, targetWidth, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assets] failed to prescale {asset}: {ex.Message}");
                return null;
            }
        }
    }
}
