namespace RevenantAuthServer.Models
{
    public record RegisterRequest(string? Username, string? Password);
    public record LoginRequest(string? Username, string? Password);
    public record RefreshRequest(string? RefreshToken);
    public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public record AuthResponse(string AccessToken, string RefreshToken, int UserId, string Username);
    public record MeResponse(int UserId, string Username, DateTime CreatedAt, DateTime LastLoginAt,
        string CurrentIp, List<LoginHistoryDto> LoginHistory);
    public record LoginHistoryDto(string Ip, DateTime At);

    // ===== Социальные (игроки, друзья, сессии) =====
    public record SessionReportRequest(string? StartedAt, string? EndedAt, string? VersionId,
        string? VersionDisplay, string? Loader, string? Status);
    public record FriendAddRequest(string? Username);
    public record PlayerStatsResponse(long TotalSessions, long TotalSeconds,
        string? FavoriteVersion, DateTime? LastPlayedAt);
    public record PlayerProfileResponse(string Username, DateTime CreatedAt, string FriendStatus, bool IsOnline, string PresenceStatus, string? CurrentVersion, PlayerStatsResponse Stats);
    public record PlayerSearchResult(string Username, string FriendStatus);
    public record FriendRequestResponse(string Username, DateTime CreatedAt);
    public record PresenceRequest(string? Status, string? Version);
    public record PresenceResponse(bool IsOnline, string PresenceStatus, string? CurrentVersion, DateTime? LastSeenAt);
    public record SocialNotificationResponse(long Id, string Type, string? ActorUsername, string Text, DateTime CreatedAt, bool IsRead);
    public record SendMessageRequest(string? Text);
    public record NotificationReadRequest(long Id);
    public record ChatMessageResponse(long Id, string SenderUsername, string RecipientUsername, string Text, DateTime CreatedAt, bool IsMine);
    public record SendMessageResponse(bool Ok, string? Error, ChatMessageResponse? Message);
}
