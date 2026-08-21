using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class AccountView : UserControl
    {
        private AccountViewModel ViewModel => (AccountViewModel)DataContext!;

        public AccountView()
        {
            InitializeComponent();

            // Подгружаем профиль с сервера при открытии страницы
            AttachedToVisualTree += async (_, _) => await ViewModel.LoadAsync();
        }

        private async void ChangePassword_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.ChangePasswordAsync();
        }

        private async void Logout_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.LogoutAsync();
        }
    }
}
