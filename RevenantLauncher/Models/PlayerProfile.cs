using System;

namespace RevenantLauncher.Models
{
    /// <summary>
    /// Статус отношений с игроком (как в API воркера и сервера):
    /// none — чужие, friend — друзья, outgoing — я отправил заявку, incoming — мне отправили заявку.
    /// </summary>
    public static class FriendStatuses
    {
        public const string None = "none";
        public const string Friend = "friend";
        public const string Outgoing = "outgoing";
        public const string Incoming = "incoming";
    }

    /// <summary>Результат поиска игрока (из /api/users/search)</summary>
    public class PlayerSearchResult
    {
        public string Username { get; set; } = string.Empty;
        public string FriendStatus { get; set; } = FriendStatuses.None;

        public bool IsFriend => FriendStatus == FriendStatuses.Friend;
        public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username[..1].ToUpper();

        /// <summary>Подпись под ником в результатах поиска</summary>
        public string FriendStatusText => FriendStatus switch
        {
            FriendStatuses.Friend => "✓ в друзьях",
            FriendStatuses.Outgoing => "заявка отправлена",
            FriendStatuses.Incoming => "хочет дружить",
            _ => ""
        };

        public bool HasFriendStatus => FriendStatus != FriendStatuses.None;
    }

    /// <summary>Статистика игрока (из профиля)</summary>
    public class PlayerStatsDto
    {
        public long TotalSessions { get; set; }
        public long TotalSeconds { get; set; }
        public string? FavoriteVersion { get; set; }
        public DateTime? LastPlayedAt { get; set; }
    }

    /// <summary>Профиль игрока (из /api/users/{username} и /api/friends)</summary>
    public class PlayerProfileDto
    {
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string FriendStatus { get; set; } = FriendStatuses.None;
        public bool IsOnline { get; set; }
        public string PresenceStatus { get; set; } = "offline";
        public string? CurrentVersion { get; set; }
        public PlayerStatsDto? Stats { get; set; }
    }

    /// <summary>Входящая заявка в друзья (из /api/friends/requests)</summary>
    public class FriendRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Обёртка заявки для UI</summary>
    public class FriendRequest
    {
        public FriendRequest(FriendRequestDto dto)
        {
            Username = dto.Username;
            CreatedAt = dto.CreatedAt;
        }

        public string Username { get; }
        public DateTime CreatedAt { get; }

        public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username[..1].ToUpper();

        public string CreatedText => CreatedAt == default
            ? ""
            : "с " + CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");
    }

    /// <summary>Результат отправки заявки: Ok + новый статус отношений (или ошибка)</summary>
    public class FriendActionResult
    {
        public bool Ok { get; set; }
        public string Status { get; set; } = FriendStatuses.None;
        public string? Error { get; set; }
    }

    /// <summary>Тело ответа POST /api/friends (статус после отправки)</summary>
    public class FriendAddResultDto
    {
        public string? Status { get; set; }
    }

    /// <summary>Обёртка для отображения профиля в UI</summary>
    public class PlayerProfile
    {
        public PlayerProfile(PlayerProfileDto dto)
        {
            Username = dto.Username;
            CreatedAt = dto.CreatedAt;
            FriendStatus = dto.FriendStatus;
            Stats = dto.Stats;
            IsOnline = dto.IsOnline;
            PresenceStatus = dto.PresenceStatus;
            CurrentVersion = dto.CurrentVersion;
        }

        public string Username { get; set; }
        public DateTime CreatedAt { get; }
        public string FriendStatus { get; set; }
        public PlayerStatsDto? Stats { get; }
        public bool IsOnline { get; set; }
        public string PresenceStatus { get; set; } = "offline";
        public string? CurrentVersion { get; set; }

        public string OnlineStatusText => !IsOnline ? "не в сети" :
            PresenceStatus == "playing" ? $"играет{(string.IsNullOrWhiteSpace(CurrentVersion) ? "" : " в " + CurrentVersion)}" : "онлайн";

        public string OnlineStatusColor => !IsOnline ? "#888888" :
            PresenceStatus == "playing" ? "#66BB6A" : "#B388FF";

        public string Initial => string.IsNullOrEmpty(Username) ? "?" : Username[..1].ToUpper();

        public bool IsFriend => FriendStatus == FriendStatuses.Friend;

        /// <summary>Бейдж в шапке профиля: «в друзьях» / «заявка отправлена» / «хочет дружить»</summary>
        public string FriendBadgeText => FriendStatus switch
        {
            FriendStatuses.Friend => "в друзьях",
            FriendStatuses.Outgoing => "заявка отправлена",
            FriendStatuses.Incoming => "хочет дружить",
            _ => ""
        };

        public bool HasFriendBadge => FriendStatus != FriendStatuses.None;

        public string CreatedAtText => CreatedAt == default
            ? ""
            : "с " + CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");

        public string TotalSessionsText => Stats?.TotalSessions.ToString() ?? "0";

        public string PlayTimeText => FormatTime(Stats?.TotalSeconds ?? 0);

        public string FavoriteVersionText => string.IsNullOrEmpty(Stats?.FavoriteVersion)
            ? "—"
            : Stats.FavoriteVersion;

        public string LastPlayedText => Stats?.LastPlayedAt is DateTime dt
            ? "играл " + RelativeTime(dt.ToLocalTime())
            : "ещё не играл";

        /// <summary>Формат наигранного времени: «2 ч 15 мин», «45 мин», «30 сек»</summary>
        public static string FormatTime(long seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours} ч {t.Minutes} мин";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} мин";
            return $"{t.TotalSeconds:0} сек";
        }

        /// <summary>Относительное время: «только что», «5 мин назад», «вчера», «12.08.2026»</summary>
        public static string RelativeTime(DateTime localTime)
        {
            var diff = DateTime.Now - localTime;
            if (diff < TimeSpan.Zero) diff = TimeSpan.Zero;

            if (diff.TotalMinutes < 1) return "только что";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} мин назад";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} ч назад";
            if (diff.TotalDays < 2) return "вчера";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} дн назад";
            return localTime.ToString("dd.MM.yyyy");
        }
    }
}
