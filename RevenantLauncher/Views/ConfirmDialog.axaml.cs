using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class ConfirmDialog : UserControl
    {
        private ConfirmDialogViewModel ViewModel => (ConfirmDialogViewModel)DataContext!;

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        private void Confirm_Click(object? sender, RoutedEventArgs e) => ViewModel.Confirm();
        private void Cancel_Click(object? sender, RoutedEventArgs e) => ViewModel.Cancel();
    }
}
