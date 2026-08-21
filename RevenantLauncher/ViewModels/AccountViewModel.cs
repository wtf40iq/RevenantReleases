using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    /// <summary>Страница «Аккаунт»: профиль Revenant, смена пароля, выход</summary>
    public class AccountViewModel : ViewModelBase
    {
        private string _username = "";
        private int _userId;
        private string _createdAtText = "";
        private string _lastLoginText = "";
        private string _currentIp = "";
        private string _loginHistoryText = "Пока нет входов";

        private string _currentPassword = "";
        private string _newPassword = "";
        private string _confirmPassword = "";
        private string _errorMessage = "";
        private string _successMessage = "";
        private bool _isBusy;
        private bool _isLoaded;

        // ===== Профиль =====

        public string Username => _username;
        public int UserId => _userId;
        public string CreatedAtText => _createdAtText;
        public string LastLoginText => _lastLoginText;

        /// <summary>Текущий IP клиента (по данным сервера)</summary>
        public string CurrentIp => string.IsNullOrEmpty(_currentIp) ? "—" : _currentIp;

        /// <summary>Последние входы: дата + IP, по строке на запись</summary>
        public string LoginHistoryText => _loginHistoryText;

        /// <summary>Первая буква ника для аватара</summary>
        public string Initial => string.IsNullOrEmpty(_username) ? "?" : _username[..1].ToUpper();

        // ===== Смена пароля =====

        public string CurrentPassword
        {
            get => _currentPassword;
            set
            {
                if (SetProperty(ref _currentPassword, value))
                {
                    ErrorMessage = "";
                    SuccessMessage = "";
                }
            }
        }

        public string NewPassword
        {
            get => _newPassword;
            set
            {
                if (SetProperty(ref _newPassword, value))
                {
                    ErrorMessage = "";
                    SuccessMessage = "";
                }
            }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                if (SetProperty(ref _confirmPassword, value))
                {
                    ErrorMessage = "";
                    SuccessMessage = "";
                }
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

        public string SuccessMessage
        {
            get => _successMessage;
            set
            {
                if (SetProperty(ref _successMessage, value))
                    OnPropertyChanged(nameof(HasSuccess));
            }
        }

        public bool HasSuccess => !string.IsNullOrEmpty(SuccessMessage);

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        /// <summary>Загружает профиль с сервера. Вызывается при открытии страницы.</summary>
        public async Task LoadAsync()
        {
            // Имя показываем сразу из локальной сессии, остальное догрузится с сервера
            if (!_isLoaded)
            {
                _username = AuthService.Instance.CurrentUsername;
                OnPropertyChanged(nameof(Username));
                OnPropertyChanged(nameof(Initial));
            }

            var profile = await AuthService.Instance.FetchProfileAsync();
            if (profile == null)
                return;

            _username = profile.Username;
            _userId = profile.UserId;
            _createdAtText = profile.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");
            _lastLoginText = profile.LastLoginAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            _currentIp = profile.CurrentIp;

            if (profile.LoginHistory != null && profile.LoginHistory.Count > 0)
            {
                _loginHistoryText = string.Join("\n", profile.LoginHistory.Select(h =>
                    $"{h.At.ToLocalTime():dd.MM.yyyy HH:mm}  —  {(string.IsNullOrEmpty(h.Ip) ? "неизвестный IP" : h.Ip)}"));
            }
            else
            {
                _loginHistoryText = "Пока нет входов";
            }

            _isLoaded = true;

            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(UserId));
            OnPropertyChanged(nameof(Initial));
            OnPropertyChanged(nameof(CreatedAtText));
            OnPropertyChanged(nameof(LastLoginText));
            OnPropertyChanged(nameof(CurrentIp));
            OnPropertyChanged(nameof(LoginHistoryText));
        }

        public async Task ChangePasswordAsync()
        {
            if (IsBusy) return;

            if (string.IsNullOrEmpty(CurrentPassword))
            {
                ErrorMessage = "Введи текущий пароль";
                return;
            }

            if (NewPassword.Length < 6)
            {
                ErrorMessage = "Новый пароль должен быть минимум 6 символов";
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                ErrorMessage = "Пароли не совпадают";
                return;
            }

            if (CurrentPassword == NewPassword)
            {
                ErrorMessage = "Новый пароль совпадает с текущим";
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = "";
                SuccessMessage = "";

                var result = await AuthService.Instance.ChangePasswordAsync(CurrentPassword, NewPassword);

                if (result.Success)
                {
                    SuccessMessage = "Пароль изменён";
                    CurrentPassword = "";
                    NewPassword = "";
                    ConfirmPassword = "";
                    ToastService.Instance.ShowSuccess("Аккаунт", "Пароль изменён");
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

        /// <summary>Выход из аккаунта с перезапуском лаунчера — при старте покажется окно входа</summary>
        public async Task LogoutAsync()
        {
            await AuthService.Instance.LogoutAsync();

            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Account] Restart failed: {ex.Message}");
            }

            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Shutdown();
            }
        }
    }
}
