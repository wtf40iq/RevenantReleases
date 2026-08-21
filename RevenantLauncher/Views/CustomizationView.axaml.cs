using Avalonia.Controls;
using Avalonia.Input;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class CustomizationView : UserControl
    {
        private CustomizationViewModel ViewModel => (CustomizationViewModel)DataContext!;

        public CustomizationView()
        {
            InitializeComponent();
        }

        private void StandartCard_Click(object? sender, PointerPressedEventArgs e)
            => ViewModel.SelectStandart();

        private void NewCard_Click(object? sender, PointerPressedEventArgs e)
            => ViewModel.SelectNew();

        private void BannerCard_Click(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Avalonia.Controls.Border { Tag: string name })
                ViewModel.SelectBanner(name);
        }

        private void SidebarGradientCard_Click(object? sender, PointerPressedEventArgs e)
            => ViewModel.SelectSidebarGradient();

        private void SidebarDarkCard_Click(object? sender, PointerPressedEventArgs e)
            => ViewModel.SelectSidebarDark();
    }
}
