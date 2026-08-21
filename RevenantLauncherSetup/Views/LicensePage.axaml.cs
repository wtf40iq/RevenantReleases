using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class LicensePage : UserControl
    {
        public LicensePage()
        {
            InitializeComponent();
        }

        private void Back_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SetupViewModel vm)
                vm.GoBack();
        }

        private void Next_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SetupViewModel vm && vm.CanProceedFromLicense)
                vm.GoNext();
        }
    }
}