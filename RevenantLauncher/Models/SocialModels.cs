using System;

namespace RevenantLauncher.Models
{
    /// <summary>Состояние присутствия игрока на сервере</summary>
    public class PresenceDto
    {
        public bool IsOnline { get; set; }
        public string Status { get; set; } = "online";
        public string? Version { get; set; }
        public DateTime? LastSeenAt { get; set; }
    }

    /// <summary>Уведомление от социального API</summary>
    public class SocialNotificationDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? ActorUsername { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }

    /// <summary>Сообщение в чате</summary>
    public class ChatMessageDto
    {
        public long Id { get; set; }
        public string SenderUsername { get; set; } = string.Empty;
        public string RecipientUsername { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsMine { get; set; }

        public string SenderText => IsMine ? "Ты" : SenderUsername;
        public string CreatedText => CreatedAt.ToLocalTime().ToString("dd.MM HH:mm");
    }

    public class SendChatMessageResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public ChatMessageDto? Message { get; set; }
    }
}
