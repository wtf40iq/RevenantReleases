using System.Collections.ObjectModel;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public class BannerItem : ViewModelBase
    {
        private bool _isSelected;

        public string Name { get; set; } = "";
        public string DisplayName => Services.AssetImageService.GetBannerDisplayName(Name);
        public Avalonia.Media.Imaging.Bitmap? Preview { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class CustomizationViewModel : ViewModelBase
    {
        public ObservableCollection<BannerItem> Banners { get; } = new();

        public CustomizationViewModel()
        {
            foreach (var name in AssetImageService.AvailableBanners)
            {
                Banners.Add(new BannerItem
                {
                    Name = name,
                    Preview = AssetImageService.Instance.GetBannerPreview(name),
                    IsSelected = name == AssetImageService.CurrentBannerName
                });
            }
        }

        public bool IsStandart => ThemeService.Current == LauncherTheme.Standart;
        public bool IsNew => ThemeService.Current == LauncherTheme.New;

        public bool IsSidebarGradient => ThemeService.CurrentSidebar == SidebarStyle.Gradient;
        public bool IsSidebarDark => ThemeService.CurrentSidebar == SidebarStyle.Dark;

        public void SelectStandart()
        {
            if (IsStandart) return;

            ThemeService.Apply(LauncherTheme.Standart);
            SoundService.Instance.PlayClick();
            OnPropertyChanged(nameof(IsStandart));
            OnPropertyChanged(nameof(IsNew));
            ToastService.Instance.ShowSuccess("Тема применена", "Классический дизайн Standart");
        }

        public void SelectNew()
        {
            if (IsNew) return;

            ThemeService.Apply(LauncherTheme.New);
            SoundService.Instance.PlayClick();
            OnPropertyChanged(nameof(IsStandart));
            OnPropertyChanged(nameof(IsNew));
            ToastService.Instance.ShowSuccess("Тема применена", "Стеклянный дизайн Glass");
        }

        public void SelectBanner(string name)
        {
            AssetImageService.SelectBanner(name);
            foreach (var b in Banners)
                b.IsSelected = b.Name == name;
            SoundService.Instance.PlayClick();
            ToastService.Instance.ShowSuccess("Фон применён", AssetImageService.GetBannerDisplayName(name));
        }

        public void SelectSidebarGradient()
        {
            ThemeService.SetSidebarStyle(SidebarStyle.Gradient);
            SoundService.Instance.PlayClick();
            OnPropertyChanged(nameof(IsSidebarGradient));
            OnPropertyChanged(nameof(IsSidebarDark));
            ToastService.Instance.ShowSuccess("Меню применено", "Прозрачное меню слева");
        }

        public void SelectSidebarDark()
        {
            ThemeService.SetSidebarStyle(SidebarStyle.Dark);
            SoundService.Instance.PlayClick();
            OnPropertyChanged(nameof(IsSidebarGradient));
            OnPropertyChanged(nameof(IsSidebarDark));
            ToastService.Instance.ShowSuccess("Меню применено", "Тёмное меню слева");
        }
    }
}
