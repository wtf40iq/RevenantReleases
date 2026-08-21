namespace RevenantAuthServer.Models
{
    /// <summary>Игровая сессия, которую лаунчер отправляет после окончания игры</summary>
    public class PlaySessionRecord
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public long DurationSec { get; set; }
        public string? VersionId { get; set; }
        public string? VersionDisplay { get; set; }
        public string? Loader { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>Дружба (хранится двумя строками: A→B и B→A — взаимная)</summary>
    public class Friendship
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int FriendId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Заявка в друзья: requester_id → addressee_id, пока не принята</summary>
    public class FriendRequest
    {
        public int Id { get; set; }
        public int RequesterId { get; set; }
        public int AddresseeId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PresenceRecord
    {
        public int UserId { get; set; }
        public string Status { get; set; } = "online";
        public string? Version { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SocialNotification
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public int? ActorId { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
    }

    public class ChatMessage
    {
        public long Id { get; set; }
        public int SenderId { get; set; }
        public int RecipientId { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
