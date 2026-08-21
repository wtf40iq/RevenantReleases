using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class AddAccountDialog : UserControl
    {
        private AddAccountDialogViewModel ViewModel => (AddAccountDialogViewModel)DataContext!;

        public AddAccountDialog()
        {
            InitializeComponent();
        }

        private void TabOffline_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(AddAccountTab.Offline);
        private void TabMicrosoft_Click(object? sender, RoutedEventArgs e) => ViewModel.SetTab(AddAccountTab.Microsoft);

        private void Close_Click(object? sender, RoutedEventArgs e) => ViewModel.Close();

        private async void Add_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.AddAsync();
        }

        private async void Username_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await ViewModel.AddAsync();
        }
    }
}