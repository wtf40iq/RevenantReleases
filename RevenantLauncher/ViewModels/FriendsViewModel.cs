using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    /// <summary>
    /// Страница «Друзья»: поиск игроков, профиль со статистикой,
    /// заявки в друзья (отправить/принять/отклонить), список друзей с активностью.
    /// </summary>
    public class FriendsViewModel : ViewModelBase
    {
        /// <summary>Запрос на запуск указанной версии (нажали «Сыграть в эту версию»)</summary>
        public event Action<string>? RequestPlayVersion;

        private string _searchQuery = "";
        private bool _isSearching;
        private bool _isProfileLoading;
        private bool _isFriendsLoading;
        private string _errorMessage = "";
        private string _noResultsMessage = "";
        private PlayerProfile? _profile;
        private PlayerProfile? _chatFriend;
        private string _chatInput = "";
        private bool _isChatOpen;
        private bool _isChatLoading;
        private bool _isSendingMessage;
        private long _lastChatMessageId;
        private readonly Timer _socialRefreshTimer;
        private Timer? _chatTimer;

        public ObservableCollection<PlayerSearchResult> SearchResults { get; } = new();
        public ObservableCollection<PlayerProfile> Friends { get; } = new();
        public ObservableCollection<FriendRequest> IncomingRequests { get; } = new();
        public ObservableCollection<ChatMessageDto> ChatMessages { get; } = new();

        public FriendsViewModel()
        {
            _socialRefreshTimer = new Timer(_ => _ = RefreshFriendsSafeAsync(), null,
                TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
        }

        // ===== Поиск =====

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    // При изменении запроса сбрасываем старые результаты и сообщения
                    ErrorMessage = "";
                    NoResultsMessage = "";
                    if (SearchResults.Count > 0)
                    {
                        SearchResults.Clear();
                        OnPropertyChanged(nameof(HasResults));
                    }
                }
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            set => SetProperty(ref _isSearching, value);
        }

        public bool IsProfileLoading
        {
            get => _isProfileLoading;
            set => SetProperty(ref _isProfileLoading, value);
        }

        public bool IsFriendsLoading
        {
            get => _isFriendsLoading;
            set => SetProperty(ref _isFriendsLoading, value);
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
        public bool HasResults => SearchResults.Count > 0;

        /// <summary>«Игрок не найден» — показывается после пустого поиска</summary>
        public string NoResultsMessage
        {
            get => _noResultsMessage;
            set
            {
                if (SetProperty(ref _noResultsMessage, value))
                    OnPropertyChanged(nameof(HasNoResults));
            }
        }

        public bool HasNoResults => !string.IsNullOrEmpty(NoResultsMessage);

        // ===== Друзья и заявки =====

        public bool HasFriends => Friends.Count > 0;

        /// <summary>«Мои друзья (N)» — счётчик в заголовке</summary>
        public string FriendCountText => Friends.Count > 0 ? $"({Friends.Count})" : "";

        public bool HasRequests => IncomingRequests.Count > 0;

        /// <summary>«Заявки в друзья (N)» — счётчик в заголовке секции</summary>
        public string RequestsCountText => IncomingRequests.Count > 0 ? $"({IncomingRequests.Count})" : "";

        // ===== Чат =====

        public PlayerProfile? ChatFriend
        {
            get => _chatFriend;
            private set
            {
                if (SetProperty(ref _chatFriend, value))
                {
                    OnPropertyChanged(nameof(ChatTitle));
                    OnPropertyChanged(nameof(ChatSubtitle));
                }
            }
        }

        public bool IsChatOpen
        {
            get => _isChatOpen;
            private set => SetProperty(ref _isChatOpen, value);
        }

        public bool IsChatLoading
        {
            get => _isChatLoading;
            private set => SetProperty(ref _isChatLoading, value);
        }

        public bool IsSendingMessage
        {
            get => _isSendingMessage;
            private set => SetProperty(ref _isSendingMessage, value);
        }

        public string ChatInput
        {
            get => _chatInput;
            set => SetProperty(ref _chatInput, value);
        }

        public string ChatTitle => ChatFriend == null ? "Чат" : $"Чат с {ChatFriend.Username}";
        public string ChatSubtitle => ChatFriend == null ? "" : ChatFriend.OnlineStatusText;

        // ===== Профиль =====

        public PlayerProfile? Profile
        {
            get => _profile;
            set
            {
                if (SetProperty(ref _profile, value))
                {
                    OnPropertyChanged(nameof(HasProfile));
                    OnPropertyChanged(nameof(ShowProfileHint));
                    OnPropertyChanged(nameof(IsFriend));
                    OnPropertyChanged(nameof(FriendButtonText));
                    OnPropertyChanged(nameof(ShowIncomingActions));
                    OnPropertyChanged(nameof(HasFavoriteVersion));
                }
            }
        }

        public bool HasProfile => Profile != null;

        /// <summary>Подсказка, когда профиль ещё не выбран</summary>
        public bool ShowProfileHint => !HasProfile && !IsProfileLoading;

        public bool IsFriend => Profile?.IsFriend == true;

        /// <summary>Кнопка «Принять заявку» + «Отклонить», когда заявку прислали нам</summary>
        public bool ShowIncomingActions => Profile?.FriendStatus == FriendStatuses.Incoming;

        /// <summary>Текст главной кнопки профиля по статусу отношений</summary>
        public string FriendButtonText => Profile?.FriendStatus switch
        {
            FriendStatuses.Friend => "Убрать из друзей",
            FriendStatuses.Outgoing => "Отменить заявку",
            FriendStatuses.Incoming => "Принять заявку",
            _ => "Добавить в друзья"
        };

        /// <summary>Есть ли у показанного игрока любимая версия (для кнопки «Сыграть в эту версию»)</summary>
        public bool HasFavoriteVersion => Profile?.Stats is { } s && !string.IsNullOrEmpty(s.FavoriteVersion);

        /// <summary>«Сыграть в эту версию» — просим главный VM выбрать версию друга</summary>
        public void PlayProfileVersion()
        {
            if (HasFavoriteVersion)
                RequestPlayVersion?.Invoke(Profile!.Stats!.FavoriteVersion!);
        }

        // ===== Действия =====

        /// <summary>Открывает чат с принятым другом.</summary>
        public async Task OpenChatAsync(PlayerProfile friend)
        {
            if (!friend.IsFriend) return;
            ChatFriend = friend;
            IsChatOpen = true;
            _lastChatMessageId = 0;
            await Dispatcher.UIThread.InvokeAsync(ChatMessages.Clear);
            _chatTimer?.Dispose();
            _chatTimer = new Timer(_ => _ = RefreshChatAsync(), null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            await RefreshChatAsync();
        }

        public void CloseChat()
        {
            IsChatOpen = false;
            ChatFriend = null;
            _chatTimer?.Dispose();
            _chatTimer = null;
            ChatMessages.Clear();
        }

        public async Task RefreshChatAsync()
        {
            if (!IsChatOpen || ChatFriend == null) return;
            IsChatLoading = ChatMessages.Count == 0;
            try
            {
                var friendName = ChatFriend.Username;
                var messages = await AuthService.Instance.GetChatMessagesAsync(friendName, _lastChatMessageId);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var message in messages)
                    {
                        if (ChatMessages.Any(m => m.Id == message.Id)) continue;
                        ChatMessages.Add(message);
                        if (message.Id > _lastChatMessageId) _lastChatMessageId = message.Id;
                    }
                });
            }
            finally { IsChatLoading = false; }
        }

        public async Task SendMessageAsync()
        {
            if (ChatFriend == null || string.IsNullOrWhiteSpace(ChatInput) || IsSendingMessage) return;
            var text = ChatInput.Trim();
            ChatInput = "";
            IsSendingMessage = true;
            try
            {
                var result = await AuthService.Instance.SendChatMessageAsync(ChatFriend.Username, text);
                if (!result.Ok)
                {
                    ErrorMessage = result.Error ?? "Не удалось отправить сообщение";
                    ChatInput = text;
                    return;
                }
                if (result.Message != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ChatMessages.Add(result.Message);
                        _lastChatMessageId = Math.Max(_lastChatMessageId, result.Message.Id);
                    });
                }
            }
            finally { IsSendingMessage = false; }
        }

        /// <summary>Загрузка при открытии страницы</summary>
        public async Task LoadAsync()
        {
            await LoadFriendsAsync();
            await LoadRequestsAsync();
        }

        public async Task SearchAsync()
        {
            var q = SearchQuery.Trim();
            if (q.Length == 0)
            {
                ErrorMessage = "Введи ник игрока";
                return;
            }

            IsSearching = true;
            ErrorMessage = "";
            NoResultsMessage = "";
            try
            {
                var results = await AuthService.Instance.SearchPlayersAsync(q);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchResults.Clear();

                    if (results == null)
                    {
                        // Ошибка сети/сервера
                        ErrorMessage = "Не удалось выполнить поиск. Проверь соединение и попробуй ещё раз.";
                    }
                    else if (results.Count == 0)
                    {
                        // Никого не нашли — показываем понятное сообщение
                        NoResultsMessage = $"Игрок «{q}» не найден.";
                    }
                    else
                    {
                        foreach (var r in results)
                            SearchResults.Add(r);
                    }

                    OnPropertyChanged(nameof(HasResults));
                });
            }
            finally
            {
                IsSearching = false;
            }
        }

        public async Task ShowProfileAsync(string username)
        {
            IsProfileLoading = true;
            ErrorMessage = "";
            try
            {
                var dto = await AuthService.Instance.GetPlayerProfileAsync(username);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Profile = dto == null ? null : new PlayerProfile(dto);
                    if (dto == null)
                        ErrorMessage = "Игрок не найден";
                });
            }
            finally
            {
                IsProfileLoading = false;
            }
        }

        /// <summary>Главная кнопка профиля — действие по текущему статусу отношений</summary>
        public async Task ToggleFriendAsync()
        {
            if (Profile == null) return;

            switch (Profile.FriendStatus)
            {
                case FriendStatuses.Friend:
                case FriendStatuses.Outgoing:
                {
                    // Убрать из друзей / отменить заявку — сервер удаляет и то, и другое
                    var wasFriend = Profile.FriendStatus == FriendStatuses.Friend;
                    var ok = await AuthService.Instance.RemoveFriendAsync(Profile.Username);
                    if (!ok) return;
                    Profile.FriendStatus = FriendStatuses.None;
                    NotifyProfileChanged();
                    ToastService.Instance.ShowInfo(
                        wasFriend ? "Друг удалён" : "Заявка отменена",
                        Profile.Username);
                    break;
                }
                case FriendStatuses.Incoming:
                {
                    // Принять заявку
                    var ok = await AuthService.Instance.AcceptFriendRequestAsync(Profile.Username);
                    if (!ok) return;
                    Profile.FriendStatus = FriendStatuses.Friend;
                    NotifyProfileChanged();
                    ToastService.Instance.ShowSuccess("Вы теперь друзья!", Profile.Username);
                    break;
                }
                default:
                {
                    // Отправить заявку (сервер сам примет встречную, если она была)
                    var res = await AuthService.Instance.AddFriendAsync(Profile.Username);
                    if (!res.Ok)
                    {
                        ErrorMessage = res.Error ?? "Не удалось добавить в друзья";
                        return;
                    }

                    var becameFriend = res.Status == FriendStatuses.Friend || res.Status == "accepted";
                    Profile.FriendStatus = becameFriend ? FriendStatuses.Friend : FriendStatuses.Outgoing;
                    NotifyProfileChanged();
                    ToastService.Instance.ShowSuccess(
                        becameFriend ? "Вы теперь друзья!" : "Заявка отправлена",
                        Profile.Username);
                    break;
                }
            }

            SyncSearchResults();
            await LoadFriendsAsync();
            await LoadRequestsAsync();
        }

        /// <summary>«Отклонить» на профиле игрока, приславшего заявку</summary>
        public async Task DeclineProfileRequestAsync()
        {
            if (Profile?.FriendStatus != FriendStatuses.Incoming) return;

            var ok = await AuthService.Instance.DeclineFriendRequestAsync(Profile.Username);
            if (!ok) return;

            Profile.FriendStatus = FriendStatuses.None;
            NotifyProfileChanged();
            ToastService.Instance.ShowInfo("Заявка отклонена", Profile.Username);

            SyncSearchResults();
            await LoadRequestsAsync();
        }

        /// <summary>Удалить друга из списка</summary>
        public async Task RemoveFriendAsync(PlayerProfile friend)
        {
            var ok = await AuthService.Instance.RemoveFriendAsync(friend.Username);
            if (!ok) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Friends.Remove(friend);
                OnPropertyChanged(nameof(HasFriends));
                OnPropertyChanged(nameof(FriendCountText));

                // Если удалили того, чей профиль открыт — обновляем кнопку
                if (Profile?.Username == friend.Username)
                {
                    Profile.FriendStatus = FriendStatuses.None;
                    NotifyProfileChanged();
                }
                if (ChatFriend?.Username == friend.Username)
                    CloseChat();
            });

            SyncSearchResults();
            ToastService.Instance.ShowInfo("Друг удалён", friend.Username);
        }

        /// <summary>Принять заявку из секции «Заявки в друзья»</summary>
        public async Task AcceptRequestAsync(FriendRequest request)
        {
            var ok = await AuthService.Instance.AcceptFriendRequestAsync(request.Username);
            if (!ok) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IncomingRequests.Remove(request);
                OnPropertyChanged(nameof(HasRequests));
                OnPropertyChanged(nameof(RequestsCountText));

                // Если профиль этого игрока открыт — обновляем
                if (Profile?.Username == request.Username)
                {
                    Profile.FriendStatus = FriendStatuses.Friend;
                    NotifyProfileChanged();
                }
            });

            SyncSearchResults();
            ToastService.Instance.ShowSuccess("Вы теперь друзья!", request.Username);
            await LoadFriendsAsync();
        }

        /// <summary>Отклонить заявку из секции «Заявки в друзья»</summary>
        public async Task DeclineRequestAsync(FriendRequest request)
        {
            var ok = await AuthService.Instance.DeclineFriendRequestAsync(request.Username);
            if (!ok) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IncomingRequests.Remove(request);
                OnPropertyChanged(nameof(HasRequests));
                OnPropertyChanged(nameof(RequestsCountText));

                if (Profile?.Username == request.Username)
                {
                    Profile.FriendStatus = FriendStatuses.None;
                    NotifyProfileChanged();
                }
            });

            SyncSearchResults();
            ToastService.Instance.ShowInfo("Заявка отклонена", request.Username);
        }

        private async Task RefreshFriendsSafeAsync()
        {
            if (!AuthService.Instance.IsAuthenticated) return;
            try { await LoadFriendsAsync(); }
            catch (Exception ex) { Console.WriteLine($"[FriendsVM] Background refresh failed: {ex.Message}"); }
        }

        public async Task LoadFriendsAsync()
        {
            IsFriendsLoading = true;
            try
            {
                var list = await AuthService.Instance.GetFriendsAsync();

                // Сортируем по последней активности: кто играл недавно — выше
                list = list
                    .OrderByDescending(f => f.Stats?.LastPlayedAt)
                    .ThenBy(f => f.Username)
                    .ToList();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Friends.Clear();
                    foreach (var dto in list)
                        Friends.Add(new PlayerProfile(dto));
                    OnPropertyChanged(nameof(HasFriends));
                    OnPropertyChanged(nameof(FriendCountText));
                });

                SyncSearchResults();
            }
            finally
            {
                IsFriendsLoading = false;
            }
        }

        public async Task LoadRequestsAsync()
        {
            var list = await AuthService.Instance.GetFriendRequestsAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IncomingRequests.Clear();
                foreach (var dto in list)
                    IncomingRequests.Add(new FriendRequest(dto));
                OnPropertyChanged(nameof(HasRequests));
                OnPropertyChanged(nameof(RequestsCountText));
            });

            SyncSearchResults();
        }

        /// <summary>Синхронизируем статусы у результатов поиска (друзья + входящие)</summary>
        private void SyncSearchResults()
        {
            var friendNames = new HashSet<string>(Friends.Select(f => f.Username));
            var incomingNames = new HashSet<string>(IncomingRequests.Select(r => r.Username));

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var r in SearchResults)
                {
                    string status;
                    if (friendNames.Contains(r.Username)) status = FriendStatuses.Friend;
                    else if (incomingNames.Contains(r.Username)) status = FriendStatuses.Incoming;
                    else if (Profile?.Username == r.Username) status = Profile.FriendStatus;
                    else status = FriendStatuses.None;
                    r.FriendStatus = status;
                }
            });
        }

        private void NotifyProfileChanged()
        {
            OnPropertyChanged(nameof(IsFriend));
            OnPropertyChanged(nameof(FriendButtonText));
            OnPropertyChanged(nameof(ShowIncomingActions));
        }
    }
}
