using System;
using System.Threading.Tasks;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum AuthTab
    {
        Login,
        Register
    }

    public class AuthViewModel : ViewModelBase
    {
        private AuthTab _currentTab = AuthTab.Login;
        // Ник подставляется автоматически (из прошлой сессии) — при смене IP останется ввести пароль
        private string _username = Services.ConfigService.Instance.Data.Settings.LastRevenantUsername ?? "";
        private string _password = "";
        private string _confirmPassword = "";
        private string _errorMessage = "";
        private bool _isBusy;

        /// <summary>Вызывается после успешного входа/регистрации</summary>
        public event Action? AuthSucceeded;

        public AuthTab CurrentTab
        {
            get => _currentTab;
            set
            {
                if (SetProperty(ref _currentTab, value))
                {
                    OnPropertyChanged(nameof(IsLoginTab));
                    OnPropertyChanged(nameof(IsRegisterTab));
                    OnPropertyChanged(nameof(SubmitButtonText));
                    ErrorMessage = "";
                }
            }
        }

        public bool IsLoginTab => CurrentTab == AuthTab.Login;
        public bool IsRegisterTab => CurrentTab == AuthTab.Register;
        public string SubmitButtonText => IsLoginTab ? "Войти" : "Создать аккаунт";

        /// <summary>Текст кнопки с учётом занятости</summary>
        public string SubmitButtonDisplayText => IsBusy ? "Подожди..." : SubmitButtonText;

        /// <summary>Показывать подсказку «сервер просыпается» пока идёт запрос</summary>
        public bool ShowWaitHint => IsBusy;

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                    ErrorMessage = "";
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                    ErrorMessage = "";
            }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                if (SetProperty(ref _confirmPassword, value))
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
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(SubmitButtonDisplayText));
                    OnPropertyChanged(nameof(ShowWaitHint));
                }
            }
        }

        public void SetTab(AuthTab tab) => CurrentTab = tab;

        public async Task SubmitAsync()
        {
            if (IsBusy) return;

            var name = Username.Trim();

            // ===== Локальная валидация (те же правила, что на сервере) =====
            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorMessage = "Введи никнейм";
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

            if (string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "Введи пароль";
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = "Пароль должен быть минимум 6 символов";
                return;
            }

            if (IsRegisterTab && Password != ConfirmPassword)
            {
                ErrorMessage = "Пароли не совпадают";
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = "";

                var result = IsLoginTab
                    ? await AuthService.Instance.LoginAsync(name, Password)
                    : await AuthService.Instance.RegisterAsync(name, Password);

                if (result.Success)
                {
                    Password = "";
                    ConfirmPassword = "";
                    AuthSucceeded?.Invoke();
                }
                else
                {
                    ErrorMessage = result.Error ?? "Неизвестная ошибка";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
