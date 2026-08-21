using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncherSetup.ViewModels;

namespace RevenantLauncherSetup.Views
{
    public partial class WelcomePage : UserControl
    {
        public WelcomePage()
        {
            InitializeComponent();
        }

        private void Next_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is SetupViewModel vm)
                vm.GoNext();
        }
    }
}