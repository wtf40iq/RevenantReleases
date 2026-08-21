using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum LauncherPage
    {
        Home = 0,
        Mods = 1,
        Resources = 2,
        Settings = 3,
        History = 4,
        Customization = 5,
        Worlds = 6,
        Account = 7,
        Friends = 8
    }

    public class MainViewModel : ViewModelBase
    {
        private string _selectedVersion = "1.20.1";
        private GameVersion? _selectedGameVersion;
        private string _statusText = "Готов к игре";
        private bool _isAccountPanelOpen;
        private bool _isVersionDialogOpen;
        private bool _isAddAccountDialogOpen;
        private LauncherPage _currentPage = LauncherPage.Home;
        private bool _isLoading = false;
        private bool _isCurrentVersionInstalled;

        private bool _isLaunching;
        private float _launchProgress;
        private string _launchStatus = "";
        private bool _isGameRunning;
        private bool _isConfirmDialogOpen;

        private CancellationTokenSource? _statusResetCts;

        // Фоновый опрос входящих заявок в друзья для бейджа на чипе «Друзья»
        private int _friendRequestCount;
        private readonly Timer? _requestPollTimer;
        private readonly Timer? _presencePollTimer;
        private readonly HashSet<long> _seenNotificationIds = new();
        private bool _notificationsInitialized;

        // Соответствие процесс игры → сессия (для нескольких одновременных запусков)
        private readonly List<PlaySession> _pendingSessions = new();
        private readonly Dictionary<Process, PlaySession> _processSessions = new();

        public VersionDialogViewModel VersionDialog { get; }
        public AddAccountDialogViewModel AddAccountDialog { get; }
        public ConfirmDialogViewModel ConfirmDialog { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public ModsViewModel ModsViewModel { get; }
        public ResourcePacksViewModel ResourcePacksViewModel { get; }
        public WorldsViewModel WorldsViewModel { get; }
        public HistoryViewModel HistoryViewModel { get; }
        public CustomizationViewModel CustomizationViewModel { get; }
        public AccountViewModel AccountViewModel { get; }
        public ToastContainerViewModel ToastContainer { get; }
        public FriendsViewModel FriendsViewModel { get; }

        public ModDetailsDialogViewModel ModDetailsDialog => ModsViewModel.DetailsDialog;
        public DependencyDialogViewModel DependencyDialog => ModsViewModel.DependencyDialog;
        public ModDetailsDialogViewModel ResourcePackDetailsDialog => ResourcePacksViewModel.DetailsDialog;
        public ConfirmDialogViewModel WorldsConfirmDialog => WorldsViewModel.ConfirmDialog;

        public bool IsModDetailsOpen => ModsViewModel.IsDetailsOpen;
        public bool IsDependencyDialogOpen => ModsViewModel.IsDependencyOpen;
        public bool IsResourcePackDetailsOpen => ResourcePacksViewModel.IsDetailsOpen;
        public bool IsWorldsConfirmOpen => WorldsViewModel.IsConfirmOpen;

        public MainViewModel()
        {
            Accounts = AccountService.Instance.Accounts;
            AvailableVersions = VersionService.Instance.AvailableVersions;

            VersionDialog = new VersionDialogViewModel();
            VersionDialog.VersionSelected += OnVersionSelected;
            VersionDialog.DialogClosed += () => IsVersionDialogOpen = false;

            AddAccountDialog = new AddAccountDialogViewModel();
            AddAccountDialog.DialogClosed += () => IsAddAccountDialogOpen = false;

            ConfirmDialog = new ConfirmDialogViewModel();
            ConfirmDialog.DialogClosed += () => IsConfirmDialogOpen = false;
            AddAccountDialog.AccountAdded += async (acc) =>
            {
                await AccountService.Instance.SelectAsync(acc);
                OnPropertyChanged(nameof(SelectedAccount));
                ToastService.Instance.ShowSuccess("Аккаунт добавлен", acc.Username);
            };

            SettingsViewModel = new SettingsViewModel();
            ModsViewModel = new ModsViewModel();
            ResourcePacksViewModel = new ResourcePacksViewModel();
            WorldsViewModel = new WorldsViewModel();
            HistoryViewModel = new HistoryViewModel();
            CustomizationViewModel = new CustomizationViewModel();
            AccountViewModel = new AccountViewModel();
            FriendsViewModel = new FriendsViewModel();
            ToastContainer = new ToastContainerViewModel();

            ModsViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ModsViewModel.IsDetailsOpen))
                    OnPropertyChanged(nameof(IsModDetailsOpen));
                if (e.PropertyName == nameof(ModsViewModel.IsDependencyOpen))
                    OnPropertyChanged(nameof(IsDependencyDialogOpen));
            };

            // Активация версии из аккордеона папок модов
            ModsViewModel.RequestActivateVersion += name =>
                Dispatcher.UIThread.Post(() => ActivateVersionByName(name));

            ResourcePacksViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ResourcePacksViewModel.IsDetailsOpen))
                    OnPropertyChanged(nameof(IsResourcePackDetailsOpen));
            };

            WorldsViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WorldsViewModel.IsConfirmOpen))
                    OnPropertyChanged(nameof(IsWorldsConfirmOpen));
            };

            AccountService.Instance.SelectedAccountChanged += () =>
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SelectedAccount)));

            // «Сыграть в эту версию» из профиля друга — выбираем версию и возвращаемся на главную
            FriendsViewModel.RequestPlayVersion += name =>
                Dispatcher.UIThread.Post(() =>
                {
                    ActivateVersionByName(name);
                    NavigateTo(LauncherPage.Home);
                });

            // Обновляем чип аккаунта Revenant в сайдбаре при входе/выходе
            AuthService.Instance.AuthStateChanged += () =>
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(RevenantUsername));
                    OnPropertyChanged(nameof(RevenantInitial));
                    if (!AuthService.Instance.IsAuthenticated)
                    {
                        _seenNotificationIds.Clear();
                        _notificationsInitialized = false;
                    }
                    _ = RefreshFriendRequestCountAsync();
                    _ = SendPresenceHeartbeatAsync();
                    _ = RefreshSocialNotificationsAsync();
                });

            // Страница «Друзья» обновила заявки — синхронизируем бейдж
            FriendsViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(FriendsViewModel.RequestsCountText) ||
                    e.PropertyName == nameof(FriendsViewModel.HasRequests))
                    _ = RefreshFriendRequestCountAsync();
            };

            // Фоновый опрос: заявка видна на чипе даже без открытия вкладки
            _requestPollTimer = new Timer(_ => _ = RefreshFriendRequestCountAsync(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
            _presencePollTimer = new Timer(_ =>
            {
                _ = SendPresenceHeartbeatAsync();
                _ = RefreshSocialNotificationsAsync();
                _ = RefreshFriendRequestCountAsync();
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));

            // Список версий догрузился из сети — обновляем статус текущей версии
            VersionService.Instance.VersionsLoaded += () =>
                Dispatcher.UIThread.Post(RefreshCurrentVersionStatus);

            MinecraftService.Instance.ProgressChanged += p =>
                Dispatcher.UIThread.Post(() => LaunchProgress = p);

            MinecraftService.Instance.StatusChanged += s =>
                Dispatcher.UIThread.Post(() => LaunchStatus = s);

            MinecraftService.Instance.GameRunningChanged += running =>
                Dispatcher.UIThread.Post(() => IsGameRunning = running);

            MinecraftService.Instance.GameStarted += p =>
                Dispatcher.UIThread.Post(() =>
                {
                    // Игра реально запустилась
                    IsLaunching = false;
                    StatusText = "В игре";
                    LaunchProgress = 0;
                    LaunchStatus = "";
                    _ = AuthService.Instance.UpdatePresenceAsync("playing", _selectedGameVersion?.DisplayName ?? SelectedVersion);

                    // Привязываем процесс к ожидающей сессии
                    lock (_processSessions)
                    {
                        if (_pendingSessions.Count > 0)
                        {
                            _processSessions[p] = _pendingSessions[0];
                            _pendingSessions.RemoveAt(0);
                        }
                    }
                });

            // Аварийное завершение — показываем детали краша (коды ошибок, crash-report)
            MinecraftService.Instance.GameCrashed += (code, details, logPath) =>
                Dispatcher.UIThread.Post(() =>
                {
                    var message = details.Length > 400 ? details.Substring(0, 400) + "…" : details;
                    message += $"\nПолный лог: {logPath}";
                    ToastService.Instance.Show("Игра упала", message, ToastType.Error, 10000);
                });

            MinecraftService.Instance.GameExited += async (process, code) =>
            {
                // Завершаем сессию именно этого процесса
                PlaySession? session;
                lock (_processSessions)
                {
                    _processSessions.TryGetValue(process, out session);
                    _processSessions.Remove(process);
                }

                if (session != null)
                    await HistoryService.Instance.EndSessionAsync(session, code);
                else
                    await HistoryService.Instance.EndSessionAsync(code);

                Dispatcher.UIThread.Post(() =>
                {
                    IsLaunching = false;
                    StatusText = code == 0 ? "Игра завершена" : $"Игра упала (код {code})";
                    LaunchProgress = 0;
                    _ = AuthService.Instance.UpdatePresenceAsync("online", null);
                    LaunchStatus = "";

                    RefreshCurrentVersionStatus();

                    if (code == 0)
                        ToastService.Instance.ShowInfo("Игра завершена", "Возврат в лаунчер");
                    // при краше уведомление уже показано из GameCrashed

                    ScheduleStatusReset();
                });
            };

            InitializeFromLoadedData();
        }

        private void InitializeFromLoadedData()
        {
            try
            {
                if (Accounts.Count == 0 && ConfigService.Instance.IsLoaded)
                {
                    _ = Task.Run(async () =>
                    {
                        await AccountService.Instance.AddOfflineAsync("Player");
                        await Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(SelectedAccount)));
                    });
                }

                var settings = ConfigService.Instance.Data.Settings;
                SelectedVersion = settings.LastSelectedVersion;
                VersionDialog.SetTab(VersionTab.Vanilla);
                SettingsViewModel.LoadFromConfig();
                OnPropertyChanged(nameof(SelectedAccount));
                RefreshCurrentVersionStatus();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MainVM] Init error: " + ex.Message);
            }
        }

        /// <summary>Через 5 секунд сбрасывает статус на дефолтный</summary>
        private void ScheduleStatusReset()
        {
            _statusResetCts?.Cancel();
            _statusResetCts = new CancellationTokenSource();
            var token = _statusResetCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000, token);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusText = DefaultStatusText;
                    });
                }
                catch (TaskCanceledException) { }
            });
        }

        public ObservableCollection<AccountModel> Accounts { get; }
        public ObservableCollection<GameVersion> AvailableVersions { get; }

        public string SelectedAccount => AccountService.Instance.SelectedAccount?.Username ?? "Не выбран";

        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (SetProperty(ref _selectedVersion, value))
                {
                    ConfigService.Instance.Data.Settings.LastSelectedVersion = value;
                    _ = ConfigService.Instance.SaveAsync();
                    RefreshCurrentVersionStatus();
                }
            }
        }

        public bool IsCurrentVersionInstalled
        {
            get => _isCurrentVersionInstalled;
            set
            {
                if (SetProperty(ref _isCurrentVersionInstalled, value))
                {
                    OnPropertyChanged(nameof(PlayButtonText));
                    OnPropertyChanged(nameof(IsNotInstalled));
                }
            }
        }

        public bool IsNotInstalled => !_isCurrentVersionInstalled;

        /// <summary>Дефолтный статус: зависит от того, установлена ли выбранная версия</summary>
        private string DefaultStatusText => _isCurrentVersionInstalled ? "Готов к игре" : "Версия не установлена";
        public string PlayButtonText => _isGameRunning
            ? "Игра запущена"
            : (_isCurrentVersionInstalled ? "Играть" : "Скачать и играть");

        private void RefreshCurrentVersionStatus()
        {
            var version = ResolveSelectedVersion();

            if (version == null)
            {
                IsCurrentVersionInstalled = false;
                if (!IsLaunching) StatusText = DefaultStatusText;
                return;
            }

            VersionService.Instance.RefreshInstallStatus(version);
            IsCurrentVersionInstalled = version.IsInstalled;
            if (!IsLaunching) StatusText = DefaultStatusText;
        }

        /// <summary>Восстанавливает GameVersion по DisplayName: из списка или собирая из частей
        /// (нужно для сохранённых версий вида "1.21.11 Fabric" после перезапуска)</summary>
        private GameVersion? ResolveSelectedVersion()
        {
            if (_selectedGameVersion != null && _selectedGameVersion.DisplayName == SelectedVersion)
                return _selectedGameVersion;

            var version = AvailableVersions.FirstOrDefault(v => v.DisplayName == SelectedVersion)
                ?? AvailableVersions.FirstOrDefault(v => v.Id == SelectedVersion);
            if (version != null) return version;

            var parts = SelectedVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var mcVer = parts[0];
                var loaderType = parts[1].ToLower();
                var loaderVersion = parts.Length >= 3 ? parts[2] : null;

                var type = loaderType switch
                {
                    "fabric" => VersionType.Fabric,
                    "forge" => VersionType.Forge,
                    "neoforge" => VersionType.NeoForge,
                    "quilt" => VersionType.Quilt,
                    _ => VersionType.Release
                };

                return new GameVersion
                {
                    Id = $"{mcVer}-{loaderType}",
                    DisplayName = SelectedVersion,
                    Type = type,
                    LoaderVersion = loaderVersion
                };
            }

            return null;
        }

        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public bool IsAccountPanelOpen { get => _isAccountPanelOpen; set => SetProperty(ref _isAccountPanelOpen, value); }
        public bool IsVersionDialogOpen { get => _isVersionDialogOpen; set => SetProperty(ref _isVersionDialogOpen, value); }
        public bool IsAddAccountDialogOpen { get => _isAddAccountDialogOpen; set => SetProperty(ref _isAddAccountDialogOpen, value); }
        public bool IsConfirmDialogOpen { get => _isConfirmDialogOpen; set => SetProperty(ref _isConfirmDialogOpen, value); }

        /// <summary>Запущена ли сейчас хотя бы одна копия игры</summary>
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (SetProperty(ref _isGameRunning, value))
                    OnPropertyChanged(nameof(PlayButtonText));
            }
        }

        public LauncherPage CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(IsHomePage));
                    OnPropertyChanged(nameof(IsModsPage));
                    OnPropertyChanged(nameof(IsResourcesPage));
                    OnPropertyChanged(nameof(IsSettingsPage));
                    OnPropertyChanged(nameof(IsHistoryPage));
                    OnPropertyChanged(nameof(IsCustomizationPage));
                    OnPropertyChanged(nameof(IsWorldsPage));
                    OnPropertyChanged(nameof(IsAccountPage));
                    OnPropertyChanged(nameof(IsFriendsPage));

                    OnPropertyChanged(nameof(NavHomeActive));
                    OnPropertyChanged(nameof(NavModsActive));
                    OnPropertyChanged(nameof(NavResourcesActive));
                    OnPropertyChanged(nameof(NavSettingsActive));
                    OnPropertyChanged(nameof(NavHistoryActive));
                    OnPropertyChanged(nameof(NavCustomizationActive));
                    OnPropertyChanged(nameof(NavWorldsActive));
                    OnPropertyChanged(nameof(NavAccountActive));
                    OnPropertyChanged(nameof(NavFriendsActive));

                    if (value == LauncherPage.Mods)
                        _ = Task.Run(async () => await ModsViewModel.InitializeAsync());
                    if (value == LauncherPage.Resources)
                        _ = Task.Run(async () => await ResourcePacksViewModel.InitializeAsync());
                    if (value == LauncherPage.Worlds)
                        _ = Task.Run(async () => await WorldsViewModel.InitializeAsync());
                    if (value == LauncherPage.Account)
                        _ = Task.Run(async () => await AccountViewModel.LoadAsync());
                    if (value == LauncherPage.Friends)
                        _ = Task.Run(async () => await FriendsViewModel.LoadAsync());
                }
            }
        }

        public bool IsFriendsPage => CurrentPage == LauncherPage.Friends;

        public bool IsHomePage => CurrentPage == LauncherPage.Home;
        public bool IsModsPage => CurrentPage == LauncherPage.Mods;
        public bool IsResourcesPage => CurrentPage == LauncherPage.Resources;
        public bool IsSettingsPage => CurrentPage == LauncherPage.Settings;
        public bool IsHistoryPage => CurrentPage == LauncherPage.History;
        public bool IsCustomizationPage => CurrentPage == LauncherPage.Customization;
        public bool IsWorldsPage => CurrentPage == LauncherPage.Worlds;
        public bool IsAccountPage => CurrentPage == LauncherPage.Account;

        public bool NavFriendsActive => IsFriendsPage;

        public bool NavHomeActive => IsHomePage;
        public bool NavModsActive => IsModsPage;
        public bool NavResourcesActive => IsResourcesPage;
        public bool NavSettingsActive => IsSettingsPage;
        public bool NavHistoryActive => IsHistoryPage;
        public bool NavCustomizationActive => IsCustomizationPage;
        public bool NavWorldsActive => IsWorldsPage;
        public bool NavAccountActive => IsAccountPage;

        /// <summary>Имя аккаунта Revenant для чипа внизу сайдбара</summary>
        public string RevenantUsername => AuthService.Instance.CurrentUsername;

        /// <summary>Первая буква ника для аватара чипа</summary>
        public string RevenantInitial =>
            string.IsNullOrEmpty(RevenantUsername) ? "?" : RevenantUsername[..1].ToUpper();

        // ===== Бейдж заявок в друзья =====

        /// <summary>Число входящих заявок в друзья (бейдж на чипе «Друзья»)</summary>
        public int FriendRequestCount
        {
            get => _friendRequestCount;
            set
            {
                if (SetProperty(ref _friendRequestCount, value))
                {
                    OnPropertyChanged(nameof(HasFriendRequests));
                    OnPropertyChanged(nameof(FriendRequestBadgeText));
                }
            }
        }

        public bool HasFriendRequests => FriendRequestCount > 0;

        /// <summary>«9+», если заявок больше девяти</summary>
        public string FriendRequestBadgeText => FriendRequestCount > 9 ? "9+" : FriendRequestCount.ToString();

        /// <summary>Обновляет счётчик входящих заявок (фоновый опрос + события)</summary>
        private async Task RefreshFriendRequestCountAsync()
        {
            if (!AuthService.Instance.IsAuthenticated)
            {
                await Dispatcher.UIThread.InvokeAsync(() => FriendRequestCount = 0);
                return;
            }

            try
            {
                var requests = await AuthService.Instance.GetFriendRequestsAsync();
                var count = requests?.Count ?? 0;
                await Dispatcher.UIThread.InvokeAsync(() => FriendRequestCount = count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainVM] Friend request count refresh failed: {ex.Message}");
            }
        }

        private async Task SendPresenceHeartbeatAsync()
        {
            if (!AuthService.Instance.IsAuthenticated) return;
            var status = MinecraftService.Instance.IsGameRunning ? "playing" : "online";
            await AuthService.Instance.UpdatePresenceAsync(status, status == "playing" ? SelectedVersion : null);
        }

        private async Task RefreshSocialNotificationsAsync()
        {
            if (!AuthService.Instance.IsAuthenticated) return;
            try
            {
                var notifications = await AuthService.Instance.GetNotificationsAsync();
                var fresh = notifications.Where(n => !_seenNotificationIds.Contains(n.Id)).ToList();
                if (!_notificationsInitialized)
                {
                    foreach (var n in notifications) _seenNotificationIds.Add(n.Id);
                    _notificationsInitialized = true;
                    return;
                }

                foreach (var n in fresh)
                {
                    _seenNotificationIds.Add(n.Id);
                    ToastService.Instance.ShowInfo(
                        n.Type == "message" ? "Новое сообщение" : "Уведомление",
                        n.Text);
                }

                if (fresh.Count > 0)
                    await AuthService.Instance.MarkNotificationsReadAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MainVM] Notifications refresh failed: {ex.Message}");
            }
        }

        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        public bool IsLaunching
        {
            get => _isLaunching;
            set
            {
                if (SetProperty(ref _isLaunching, value))
                    OnPropertyChanged(nameof(CanLaunch));
            }
        }

        public bool CanLaunch => !IsLaunching;

        public float LaunchProgress
        {
            get => _launchProgress;
            set
            {
                if (SetProperty(ref _launchProgress, value))
                    OnPropertyChanged(nameof(LaunchProgressPercent));
            }
        }

        public double LaunchProgressPercent => LaunchProgress * 100;
        public string LaunchStatus { get => _launchStatus; set => SetProperty(ref _launchStatus, value); }

        public void ToggleAccountPanel() => IsAccountPanelOpen = !IsAccountPanelOpen;
        public void CloseAccountPanel() => IsAccountPanelOpen = false;

        public void OpenVersionDialog()
        {
            VersionDialog.SetTab(VersionTab.Vanilla);
            VersionDialog.ForceRefresh();
            IsVersionDialogOpen = true;
        }

        public void CloseVersionDialog() => IsVersionDialogOpen = false;

        public void OpenAddAccountDialog()
        {
            AddAccountDialog.SetTab(AddAccountTab.Offline);
            IsAddAccountDialogOpen = true;
        }

        public void CloseAddAccountDialog() => IsAddAccountDialogOpen = false;

        public void NavigateTo(LauncherPage page) => CurrentPage = page;

        /// <summary>Активация версии из меню папок модов: версия становится текущей для запуска</summary>
        public void ActivateVersionByName(string displayName)
        {
            _selectedGameVersion = null;
            SelectedVersion = displayName;
            _selectedGameVersion = ResolveSelectedVersion();

            if (_selectedGameVersion != null)
            {
                VersionService.Instance.RefreshInstallStatus(_selectedGameVersion);
                IsCurrentVersionInstalled = _selectedGameVersion.IsInstalled;
                if (!IsLaunching) StatusText = DefaultStatusText;
            }

            _ = ModsViewModel.OnVersionChangedAsync(displayName);
            ToastService.Instance.ShowInfo("Версия выбрана", displayName);
        }

        private void OnVersionSelected(GameVersion version)
        {
            _selectedGameVersion = version;
            SelectedVersion = version.DisplayName;
            VersionService.Instance.RefreshInstallStatus(version);
            IsCurrentVersionInstalled = version.IsInstalled;

            // Вкладка модов должна работать с папкой выбранной версии
            _ = ModsViewModel.OnVersionChangedAsync(version.DisplayName);

            ToastService.Instance.ShowInfo("Версия выбрана", version.DisplayName);
        }

        public async void SelectAccount(AccountModel account)
        {
            await AccountService.Instance.SelectAsync(account);
            OnPropertyChanged(nameof(SelectedAccount));
        }

        public async void RemoveAccount(AccountModel account)
        {
            var name = account.Username;
            await AccountService.Instance.RemoveAsync(account);
            OnPropertyChanged(nameof(SelectedAccount));
            ToastService.Instance.ShowInfo("Аккаунт удалён", name);
        }

        public async Task LaunchGameAsync()
        {
            if (IsLaunching) return;

            // Если игра уже запущена — спрашиваем, запускать ли вторую копию
            if (MinecraftService.Instance.IsGameRunning)
            {
                IsConfirmDialogOpen = true;
                var launchSecond = await ConfirmDialog.ShowAsync(
                    "Игра уже запущена",
                    "Minecraft сейчас работает. Запустить вторую копию игры?",
                    "Запустить", "Отмена");
                IsConfirmDialogOpen = false;

                if (!launchSecond) return;
            }

            // Отменяем сброс статуса если он был запланирован
            _statusResetCts?.Cancel();

            var account = AccountService.Instance.SelectedAccount;
            if (account == null)
            {
                StatusText = "Нет выбранного аккаунта!";
                ToastService.Instance.ShowError("Ошибка", "Нет выбранного аккаунта");
                ScheduleStatusReset();
                return;
            }

            GameVersion? version = ResolveSelectedVersion();

            if (version == null)
            {
                StatusText = "Версия не найдена!";
                ToastService.Instance.ShowError("Ошибка", $"Версия '{SelectedVersion}' не найдена в списке");
                ScheduleStatusReset();
                return;
            }

            // Версия могла быть собрана из имени — освежаем статус установки,
            // чтобы показать "Запуск..." вместо "Скачивание..."
            VersionService.Instance.RefreshInstallStatus(version);

            var playSession = HistoryService.Instance.StartSession(version, account);
            lock (_processSessions) _pendingSessions.Add(playSession);

            try
            {
                IsLaunching = true;
                StatusText = version.IsInstalled ? "Запуск..." : "Скачивание...";
                LaunchProgress = 0;

                if (!version.IsInstalled)
                    ToastService.Instance.ShowInfo("Скачивание", $"Загружаем {version.DisplayName}...");
                else
                    ToastService.Instance.ShowInfo("Запуск", $"Готовим {version.DisplayName}...");

                var settings = ConfigService.Instance.Data.Settings;

                await MinecraftService.Instance.LaunchAsync(version, account);

                ToastService.Instance.ShowSuccess("Игра запущена", version.DisplayName);

                VersionService.Instance.RefreshInstallStatus(version);
                IsCurrentVersionInstalled = version.IsInstalled;

                if (settings.CloseLauncherOnGameStart)
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime
                        is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_processSessions) _pendingSessions.Remove(playSession);

                StatusText = "Ошибка запуска";
                LaunchStatus = ex.Message;
                IsLaunching = false;
                ToastService.Instance.ShowError("Ошибка запуска", ex.Message);

                await HistoryService.Instance.MarkFailedAsync(ex.Message);
                ScheduleStatusReset();
            }
        }
    }
}