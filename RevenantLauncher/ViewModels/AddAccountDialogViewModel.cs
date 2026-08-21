using System;
using System.Threading.Tasks;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum AddAccountTab
    {
        Offline,
        Microsoft
    }

    public class AddAccountDialogViewModel : ViewModelBase
    {
        private AddAccountTab _currentTab = AddAccountTab.Offline;
        private string _username = "";
        private string _errorMessage = "";
        private bool _isBusy;

        public event Action? DialogClosed;
        public event Action<AccountModel>? AccountAdded;

        public AddAccountTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsOfflineTab));
                    OnPropertyChanged(nameof(IsMicrosoftTab));
                    ErrorMessage = "";
                }
            }
        }

        public bool IsOfflineTab => CurrentTab == AddAccountTab.Offline;
        public bool IsMicrosoftTab => CurrentTab == AddAccountTab.Microsoft;

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                    ErrorMessage = "";
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public void SetTab(AddAccountTab tab) => CurrentTab = tab;

        public void Close()
        {
            Username = "";
            ErrorMessage = "";
            DialogClosed?.Invoke();
        }

        public async Task AddAsync()
        {
            if (IsBusy) return;

            if (CurrentTab == AddAccountTab.Microsoft)
            {
                ErrorMessage = "Microsoft-аутентификация пока не реализована";
                return;
            }

            // Offline
            var name = Username.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorMessage = "Введите никнейм";
                return;
            }

            if (name.Length < 3 || name.Length > 16)
            {
                ErrorMessage = "Ник должен быть от 3 до 16 символов";
                return;
            }

            foreach (var ch in name)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '_'))
                {
                    ErrorMessage = "Только буквы, цифры и _";
                    return;
                }
            }

            try
            {
                IsBusy = true;
                await AccountService.Instance.AddOfflineAsync(name);

                var added = AccountService.Instance.Accounts
                    .FirstOrDefaultReverse(a => a.Username == name);

                if (added != null)
                    AccountAdded?.Invoke(added);

                Username = "";
                DialogClosed?.Invoke();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    internal static class EnumerableExt
    {
        public static T? FirstOrDefaultReverse<T>(this System.Collections.Generic.IEnumerable<T> src, Func<T, bool> pred)
        {
            T? last = default;
            foreach (var i in src)
                if (pred(i)) last = i;
            return last;
        }
    }
}