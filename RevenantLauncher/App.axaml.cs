using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RevenantLauncher.Services;
using RevenantLauncher.Views;

namespace RevenantLauncher
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            PathService.EnsureDirectoriesExist();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var splash = new SplashWindow();
                desktop.MainWindow = splash;
                splash.Show();

                _ = LoadAndShowMainAsync(desktop, splash);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async Task LoadAndShowMainAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
        {
            try
            {
                // Даём splash отрисоваться (минимум)
                await Task.Delay(100);

                // Прогрев предмасштабированных ассетов (баннер/логотипы),
                // чтобы главный экран не масштабировал 4K каждый кадр
                await Dispatcher.UIThread.InvokeAsync(() => AssetImageService.Instance.Initialize());

                // ЭТАП 1: Конфиг и аккаунты (быстро, локально)
                splash.UpdateStatus("Загрузка настроек...", 0.2);
                await ConfigService.Instance.LoadAsync();
                AccountService.Instance.Load();
                HistoryService.Instance.Load();

                // Фон мог быть выбран в прошлый раз — применяем после загрузки конфига
                AssetImageService.ApplyBannerFromConfig();

                // ЭТАП 1.5: Аккаунт лаунчера (автовход по refresh-токену или окно входа)
                splash.UpdateStatus("Проверка аккаунта...", 0.35);
                if (!await AuthService.Instance.TryRestoreSessionAsync())
                {
                    bool authOk = await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        // Splash больше не поверх — окно входа показывается над ним
                        splash.Topmost = false;

                        var authWindow = new AuthWindow();
                        var tcs = new TaskCompletionSource<bool>();
                        authWindow.Closed += (_, _) => tcs.TrySetResult(authWindow.Authenticated);
                        authWindow.Show();

                        bool ok = await tcs.Task;
                        splash.Topmost = true;
                        return ok;
                    });

                    if (!authOk)
                    {
                        // Пользователь закрыл окно входа — выходим из лаунчера
                        splash.Close();
                        desktop.Shutdown();
                        return;
                    }
                }

                splash.UpdateStatus("Получение версий Minecraft...", 0.5);

                // ЭТАП 2: Версии Minecraft (медленно, сеть) — параллельно с прогревом
                var versionsTask = VersionService.Instance.LoadAsync();

                // Пока грузятся версии — прогреваем что-то ещё
                await Task.WhenAll(versionsTask);

                splash.UpdateStatus("Готово!", 0.95);

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // Тема должна быть применена до создания MainWindow,
                    // чтобы все DynamicResource сразу получили нужные значения
                    ThemeService.InitializeFromConfig();

                    var main = new MainWindow();

                    await splash.RunExitAnimation();

                    main.Show();
                    desktop.MainWindow = main;

                    // Плавный скролл — прикрепляем ОДИН раз при открытии
                    // (все страницы и диалоги созданы сразу в XAML, таймер не нужен)
                    main.Opened += (_, _) =>
                    {
                        AttachToExistingScrollViewers(main);
                    };

                    splash.Close();

                    // Проверяем обновление уже после показа главного окна,
                    // чтобы сеть не задерживала запуск лаунчера.
                    _ = CheckForUpdateAsync(main);
                });
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine("[App] Init failed: " + ex.Message);
                splash.UpdateStatus("Ошибка загрузки", 0);
                await Task.Delay(2000);
                splash.Close();
            }
        }

        private static async Task CheckForUpdateAsync(MainWindow main)
        {
            try
            {
                var update = await UpdateService.CheckForUpdateAsync();
                if (update == null || !main.IsVisible) return;

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!main.IsVisible) return;
                    var updateWindow = new UpdateWindow(update);
                    await updateWindow.ShowDialog(main);
                });
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine("[Update] startup flow failed: " + ex.Message);
            }
        }

        /// <summary>Прикрепляет плавный скролл ко всем ScrollViewer окна</summary>
        private static void AttachToExistingScrollViewers(Visual visual)
        {
            foreach (var sv in visual.GetVisualDescendants().OfType<ScrollViewer>())
            {
                if (!SmoothScrollBehavior.GetIsEnabled(sv))
                    SmoothScrollBehavior.SetIsEnabled(sv, true);
            }
        }
    }
}