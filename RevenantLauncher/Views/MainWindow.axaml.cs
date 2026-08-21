using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RevenantLauncher.Behaviors;
using RevenantLauncher.Models;
using RevenantLauncher.Services;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext!;

        public MainWindow()
        {
            InitializeComponent();

            // Подменяем на предмасштабированные ассеты, чтобы не масштабировать 4K каждый кадр
            var assets = AssetImageService.Instance;
            if (assets.CurrentBanner != null && BannerBorder.Background is Avalonia.Media.ImageBrush bannerBrush)
                bannerBrush.Source = assets.CurrentBanner;
            if (assets.FonLogo != null) SidebarLogoImage.Source = assets.FonLogo;
            if (assets.LauncherLogo != null) HeroLogoImage.Source = assets.LauncherLogo;

            // Смена фона из кастомизации — применяем на лету
            AssetImageService.BannerChanged += () =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (assets.CurrentBanner != null && BannerBorder.Background is Avalonia.Media.ImageBrush brush)
                        brush.Source = assets.CurrentBanner;
                });

            // Область, относительно которой LiquidGlassLayer считает срез размытого баннера
            LiquidGlassLayer.Host = RootBorder;
        }

        // Перетаскивание окна — только за края (60px сверху/слева/справа)
        private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (e.Source is Control control)
            {
                var el = control;
                while (el != null)
                {
                    if (el is Button || el is TextBox || el is CheckBox || el is ScrollViewer)
                        return;
                    el = el.Parent as Control;
                }
            }

            var pos = e.GetPosition(this);
            const double edgeSize = 60;

            bool isEdge =
                pos.Y <= edgeSize ||
                pos.X <= edgeSize ||
                pos.X >= this.Bounds.Width - edgeSize;

            if (!isEdge) return;

            BeginMoveDrag(e);
        }

        // ===== Window controls =====
        private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            _ = AuthService.Instance.ClearPresenceAsync();
            Close();
        }

        // ===== Navigation =====
        private void NavHome_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Home);
        }

        private void NavMods_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Mods);
        }

        private void NavResources_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Resources);
        }

        private void NavWorlds_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Worlds);
        }

        private void NavSettings_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Settings);
        }

        private void NavHistory_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.History);
        }

        private void NavAccount_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Account);
        }

        private void NavCustomization_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Customization);
        }

        private void NavFriends_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.NavigateTo(LauncherPage.Friends);
        }

        // ===== Account panel =====
        private void AccountButton_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.ToggleAccountPanel();
        }

        private void AccountPanelClose_Click(object? sender, RoutedEventArgs e) => ViewModel.CloseAccountPanel();
        private void AccountConfirm_Click(object? sender, RoutedEventArgs e) => ViewModel.CloseAccountPanel();

        private void AccountItem_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AccountModel acc)
                ViewModel.SelectAccount(acc);
        }

        private void DeleteAccount_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AccountModel acc)
                ViewModel.RemoveAccount(acc);

            e.Handled = true;
        }

        private void AddAccount_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.CloseAccountPanel();
            ViewModel.OpenAddAccountDialog();
        }

        // ===== Version dialog =====
        private void VersionButton_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            ViewModel.OpenVersionDialog();
        }

        // ===== Play =====
        private async void Play_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlaySuccess();
            await ViewModel.LaunchGameAsync();
        }
    }
}