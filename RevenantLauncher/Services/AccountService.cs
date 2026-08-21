using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class AccountService
    {
        private static readonly Lazy<AccountService> _instance = new(() => new AccountService());
        public static AccountService Instance => _instance.Value;

        public ObservableCollection<AccountModel> Accounts { get; } = new();

        public AccountModel? SelectedAccount => Accounts.FirstOrDefault(a => a.IsSelected);

        public event Action? SelectedAccountChanged;
        public event Action? AccountsChanged;

        private AccountService() { }

        public void Load()
        {
            Accounts.Clear();
            foreach (var acc in ConfigService.Instance.Data.Accounts)
                Accounts.Add(acc);

            if (Accounts.Count > 0 && SelectedAccount == null)
            {
                // Восстанавливаем последний выбранный аккаунт (LastSelectedAccountId),
                // если флаг IsSelected не сохранился (например, старый конфиг)
                var lastId = ConfigService.Instance.Data.Settings.LastSelectedAccountId;
                var restored = Accounts.FirstOrDefault(a => a.Id.ToString() == lastId);
                (restored ?? Accounts[0]).IsSelected = true;
            }

            Console.WriteLine($"[AccountService] Loaded {Accounts.Count} accounts");
            AccountsChanged?.Invoke();
        }

        public async Task AddOfflineAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Никнейм не может быть пустым");

            if (Accounts.Any(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Аккаунт с таким никнеймом уже существует");

            var account = new AccountModel
            {
                Username = username,
                Type = AccountType.Offline,
                Uuid = GenerateOfflineUuid(username),
                IsSelected = Accounts.Count == 0
            };

            Accounts.Add(account);
            ConfigService.Instance.Data.Accounts.Add(account);
            await ConfigService.Instance.SaveAsync();

            Console.WriteLine($"[AccountService] Added: {username}. Total: {Accounts.Count}");

            AccountsChanged?.Invoke();
            if (account.IsSelected) SelectedAccountChanged?.Invoke();
        }

        public async Task RemoveAsync(AccountModel account)
        {
            bool wasSelected = account.IsSelected;
            Accounts.Remove(account);
            ConfigService.Instance.Data.Accounts.Remove(account);

            if (wasSelected && Accounts.Count > 0)
                Accounts[0].IsSelected = true;

            await ConfigService.Instance.SaveAsync();
            AccountsChanged?.Invoke();
            SelectedAccountChanged?.Invoke();
        }

        public async Task SelectAsync(AccountModel account)
        {
            foreach (var acc in Accounts)
                acc.IsSelected = false;

            account.IsSelected = true;
            account.LastUsedAt = DateTime.UtcNow;
            ConfigService.Instance.Data.Settings.LastSelectedAccountId = account.Id.ToString();
            await ConfigService.Instance.SaveAsync();

            SelectedAccountChanged?.Invoke();
            AccountsChanged?.Invoke();
        }

        private static string GenerateOfflineUuid(string username)
        {
            var input = "OfflinePlayer:" + username;
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hash = System.Security.Cryptography.MD5.HashData(bytes);
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash).ToString();
        }
    }
}