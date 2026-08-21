using System.Collections.ObjectModel;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public class ToastContainerViewModel : ViewModelBase
    {
        public ObservableCollection<ToastNotification> Toasts => ToastService.Instance.Toasts;
    }
}