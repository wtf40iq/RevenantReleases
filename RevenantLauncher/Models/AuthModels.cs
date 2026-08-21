using System;
using System.Collections.Generic;

namespace RevenantLauncher.Models
{
    /// <summary>Ответ сервера авторизации (register / login / refresh)</summary>
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }

    /// <summary>Тело ошибки сервера: { message }</summary>
    public class AuthErrorDto
    {
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Профиль аккаунта лаунчера (GET /api/auth/me)</summary>
    public class AuthProfileDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastLoginAt { get; set; }
        public string CurrentIp { get; set; } = string.Empty;
        public List<LoginHistoryEntry> LoginHistory { get; set; } = new();
    }

    /// <summary>Запись истории входов</summary>
    public class LoginHistoryEntry
    {
        public string Ip { get; set; } = string.Empty;
        public DateTime At { get; set; }
    }

    /// <summary>Результат операции авторизации на клиенте</summary>
    public class AuthResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }

        public static AuthResult Ok() => new() { Success = true };
        public static AuthResult Fail(string error) => new() { Success = false, Error = error };
    }
}
