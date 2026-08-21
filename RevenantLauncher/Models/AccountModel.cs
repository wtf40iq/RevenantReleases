using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RevenantLauncher.Models
{
    public enum AccountType
    {
        Offline,
        Microsoft
    }

    public class AccountModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = string.Empty;
        public string Uuid { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public AccountType Type { get; set; } = AccountType.Offline;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        public string Status => Type == AccountType.Microsoft ? "Онлайн" : "Офлайн";
        public bool IsOnline => Type == AccountType.Microsoft;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}