using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    /// <summary>
    /// Авторизация в самом лаунчере (аккаунты Revenant, не Minecraft).
    /// Общается с RevenantAuthServer: регистрация, вход, продление сессии, выход.
    /// Refresh-токен хранится зашифрованным через DPAPI (привязан к пользователю Windows).
    /// </summary>
    public class AuthService
    {
        private static readonly Lazy<AuthService> _instance = new(() => new AuthService());
        public static AuthService Instance => _instance.Value;

        // TODO: после деплоя на Render.com замени на свой URL, например
        // "https://revenant-auth.onrender.com"
         public const string ApiBaseUrl = "https://revenant-auth.skymywex.workers.dev";

        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public int CurrentUserId { get; private set; }
        public string CurrentUsername { get; private set; } = string.Empty;
        public bool IsAuthenticated { get; private set; }

        /// <summary>Access-токен живёт только в памяти (15 мин), при 401 автоматически продлевается</summary>
        public string? CurrentAccessToken { get; private set; }

        public event Action? AuthStateChanged;

        private AuthService()
        {
            // Таймаут 60 сек — бесплатный сервер Render может "просыпаться" до ~30 сек
            _http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl), Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task<AuthResult> RegisterAsync(string username, string password)
            => await SendCredentialsAsync("/api/auth/register", username, password);

        public async Task<AuthResult> LoginAsync(string username, string password)
            => await SendCredentialsAsync("/api/auth/login", username, password);

        /// <summary>Пытается восстановить сессию по сохранённому refresh-токену (автовход)</summary>
        public async Task<bool> TryRestoreSessionAsync()
        {
            if (!await RefreshSessionAsync())
                return false;

            Console.WriteLine($"[Auth] Session restored for {CurrentUsername}");
            return true;
        }

        /// <summary>Профиль текущего пользователя (для страницы «Аккаунт»)</summary>
        public async Task<AuthProfileDto?> FetchProfileAsync()
        {
            try
            {
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/auth/me"));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/auth/me"));

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<AuthProfileDto>(JsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Fetch profile failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Смена пароля аккаунта лаунчера</summary>
        public async Task<AuthResult> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            try
            {
                var response = await _http.SendAsync(ChangePasswordRequest(currentPassword, newPassword));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(ChangePasswordRequest(currentPassword, newPassword));

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Auth] Password changed for {CurrentUsername}");
                    return AuthResult.Ok();
                }

                return AuthResult.Fail(await ExtractErrorMessageAsync(response));
            }
            catch (TaskCanceledException)
            {
                return AuthResult.Fail("Сервер не отвечает. Он мог уснуть на бесплатном хостинге — подожди минуту и попробуй снова.");
            }
            catch (HttpRequestException)
            {
                return AuthResult.Fail("Не удалось подключиться к серверу. Сервер мог уснуть — подожди минуту и повтори.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Change password error: {ex.Message}");
                return AuthResult.Fail("Ошибка соединения с сервером");
            }
        }

        private HttpRequestMessage ChangePasswordRequest(string currentPassword, string newPassword)
        {
            var request = AuthorizedRequest(HttpMethod.Post, "/api/auth/change-password");
            request.Content = JsonContent.Create(new { currentPassword, newPassword });
            return request;
        }

        // ===== Социальные: поиск игроков, профили, друзья =====

        /// <summary>Поиск игроков по префиксу ника.
        /// Возвращает null при ошибке сети/сервера, пустой список — если никого не нашли.</summary>
        public async Task<List<PlayerSearchResult>?> SearchPlayersAsync(string query)
        {
            try
            {
                var url = "/api/users/search?q=" + Uri.EscapeDataString(query);
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<List<PlayerSearchResult>>(JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Search failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Публичный профиль игрока со статистикой. null — не найден/ошибка сети.</summary>
        public async Task<PlayerProfileDto?> GetPlayerProfileAsync(string username)
        {
            try
            {
                var url = "/api/users/" + Uri.EscapeDataString(username);
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<PlayerProfileDto>(JsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Get profile failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Список друзей со статистикой и активностью</summary>
        public async Task<List<PlayerProfileDto>> GetFriendsAsync()
        {
            try
            {
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/friends"));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/friends"));

                if (!response.IsSuccessStatusCode)
                    return new();

                return await response.Content.ReadFromJsonAsync<List<PlayerProfileDto>>(JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Get friends failed: {ex.Message}");
                return new();
            }
        }

        /// <summary>Отправить заявку в друзья (сервер сам примет встречную).
        /// Возвращает результат с новым статусом отношений (pending/outgoing/accepted/friend) или ошибкой.</summary>
        public async Task<FriendActionResult> AddFriendAsync(string username)
        {
            try
            {
                var request = AuthorizedRequest(HttpMethod.Post, "/api/friends");
                request.Content = JsonContent.Create(new { username });
                var response = await _http.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                {
                    request = AuthorizedRequest(HttpMethod.Post, "/api/friends");
                    request.Content = JsonContent.Create(new { username });
                    response = await _http.SendAsync(request);
                }

                if (response.IsSuccessStatusCode)
                {
                    var dto = await response.Content.ReadFromJsonAsync<FriendAddResultDto>(JsonOptions);
                    return new FriendActionResult { Ok = true, Status = dto?.Status ?? FriendStatuses.Outgoing };
                }

                return new FriendActionResult { Ok = false, Error = await ExtractErrorMessageAsync(response) };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Add friend failed: {ex.Message}");
                return new FriendActionResult { Ok = false, Error = "Ошибка соединения с сервером" };
            }
        }

        /// <summary>Убрать из друзей / отменить заявку (сервер удаляет и дружбу, и заявки между нами)</summary>
        public async Task<bool> RemoveFriendAsync(string username)
        {
            try
            {
                var url = "/api/friends/" + Uri.EscapeDataString(username);
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Delete, url));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Delete, url));

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Remove friend failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Входящие заявки в друзья</summary>
        public async Task<List<FriendRequestDto>> GetFriendRequestsAsync()
        {
            try
            {
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/friends/requests"));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/friends/requests"));

                if (!response.IsSuccessStatusCode)
                    return new();

                return await response.Content.ReadFromJsonAsync<List<FriendRequestDto>>(JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Get friend requests failed: {ex.Message}");
                return new();
            }
        }

        /// <summary>Принять заявку в друзья</summary>
        public async Task<bool> AcceptFriendRequestAsync(string username)
        {
            try
            {
                var url = "/api/friends/requests/" + Uri.EscapeDataString(username) + "/accept";
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Post, url));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Post, url));

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Accept friend request failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Отклонить заявку в друзья</summary>
        public async Task<bool> DeclineFriendRequestAsync(string username)
        {
            try
            {
                var url = "/api/friends/requests/" + Uri.EscapeDataString(username) + "/decline";
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Post, url));

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Post, url));

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Decline friend request failed: {ex.Message}");
                return false;
            }
        }

        // ===== Онлайн-статус, уведомления и чат =====

        /// <summary>Обновляет присутствие: online или playing. Fire-and-forget.</summary>
        public async Task UpdatePresenceAsync(string status, string? version = null)
        {
            if (!IsAuthenticated) return;
            try
            {
                var request = AuthorizedRequest(HttpMethod.Post, "/api/presence");
                request.Content = JsonContent.Create(new { status, version });
                var response = await _http.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                {
                    request = AuthorizedRequest(HttpMethod.Post, "/api/presence");
                    request.Content = JsonContent.Create(new { status, version });
                    await _http.SendAsync(request);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Presence update failed: {ex.Message}"); }
        }

        /// <summary>Удаляет присутствие при выходе лаунчера.</summary>
        public async Task ClearPresenceAsync()
        {
            if (!IsAuthenticated) return;
            try
            {
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Delete, "/api/presence"));
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    await _http.SendAsync(AuthorizedRequest(HttpMethod.Delete, "/api/presence"));
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Presence clear failed: {ex.Message}"); }
        }

        /// <summary>Получает непрочитанные уведомления.</summary>
        public async Task<List<SocialNotificationDto>> GetNotificationsAsync()
        {
            try
            {
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/notifications?unread=true"));
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, "/api/notifications?unread=true"));
                if (!response.IsSuccessStatusCode) return new();
                return await response.Content.ReadFromJsonAsync<List<SocialNotificationDto>>(JsonOptions) ?? new();
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Notifications failed: {ex.Message}"); return new(); }
        }

        /// <summary>Помечает уведомления прочитанными (id=0 — все).</summary>
        public async Task MarkNotificationsReadAsync(long id = 0)
        {
            try
            {
                var request = AuthorizedRequest(HttpMethod.Post, "/api/notifications/read");
                request.Content = JsonContent.Create(new { id });
                var response = await _http.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                {
                    request = AuthorizedRequest(HttpMethod.Post, "/api/notifications/read");
                    request.Content = JsonContent.Create(new { id });
                    await _http.SendAsync(request);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Mark notifications failed: {ex.Message}"); }
        }

        /// <summary>История сообщений между друзьями, начиная с afterId.</summary>
        public async Task<List<ChatMessageDto>> GetChatMessagesAsync(string username, long afterId = 0)
        {
            try
            {
                var url = "/api/chat/" + Uri.EscapeDataString(username) + "?afterId=" + afterId;
                var response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                    response = await _http.SendAsync(AuthorizedRequest(HttpMethod.Get, url));
                if (!response.IsSuccessStatusCode) return new();
                return await response.Content.ReadFromJsonAsync<List<ChatMessageDto>>(JsonOptions) ?? new();
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Chat load failed: {ex.Message}"); return new(); }
        }

        /// <summary>Отправляет сообщение другу.</summary>
        public async Task<SendChatMessageResult> SendChatMessageAsync(string username, string text)
        {
            try
            {
                var request = AuthorizedRequest(HttpMethod.Post, "/api/chat/" + Uri.EscapeDataString(username));
                request.Content = JsonContent.Create(new { text });
                var response = await _http.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                {
                    request = AuthorizedRequest(HttpMethod.Post, "/api/chat/" + Uri.EscapeDataString(username));
                    request.Content = JsonContent.Create(new { text });
                    response = await _http.SendAsync(request);
                }
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<SendChatMessageResult>(JsonOptions) ?? new SendChatMessageResult { Ok = true };
                return new SendChatMessageResult { Error = await ExtractErrorMessageAsync(response) };
            }
            catch (Exception ex) { Console.WriteLine($"[Auth] Chat send failed: {ex.Message}"); return new SendChatMessageResult { Error = "Ошибка соединения с сервером" }; }
        }

        /// <summary>Отправляет на сервер завершённую игровую сессию (для статистики).
        /// Fire-and-forget: ошибки сети молча игнорируются.</summary>
        public async Task ReportSessionAsync(PlaySession session)
        {
            if (!IsAuthenticated) return;

            try
            {
                var request = AuthorizedRequest(HttpMethod.Post, "/api/sessions");
                request.Content = JsonContent.Create(new
                {
                    startedAt = session.StartedAt.ToUniversalTime().ToString("o"),
                    endedAt = session.EndedAt?.ToUniversalTime().ToString("o"),
                    versionId = session.VersionId,
                    versionDisplay = session.VersionDisplayName,
                    loader = session.LoaderType,
                    status = session.Status.ToString()
                });
                var response = await _http.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && await RefreshSessionAsync())
                {
                    request = AuthorizedRequest(HttpMethod.Post, "/api/sessions");
                    request.Content = JsonContent.Create(new
                    {
                        startedAt = session.StartedAt.ToUniversalTime().ToString("o"),
                        endedAt = session.EndedAt?.ToUniversalTime().ToString("o"),
                        versionId = session.VersionId,
                        versionDisplay = session.VersionDisplayName,
                        loader = session.LoaderType,
                        status = session.Status.ToString()
                    });
                    await _http.SendAsync(request);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Report session failed: {ex.Message}");
            }
        }

        public async Task LogoutAsync()
        {
            var token = LoadToken();

            try
            {
                if (!string.IsNullOrEmpty(token))
                    await _http.PostAsJsonAsync("/api/auth/logout", new { refreshToken = token });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Logout request failed: {ex.Message}");
            }

            ClearToken();
            CurrentUserId = 0;
            CurrentUsername = string.Empty;
            CurrentAccessToken = null;
            IsAuthenticated = false;
            AuthStateChanged?.Invoke();
            Console.WriteLine("[Auth] Logged out");
        }

        // ===== Внутреннее =====

        private async Task<AuthResult> SendCredentialsAsync(string endpoint, string username, string password)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(endpoint, new { username, password });

                if (response.IsSuccessStatusCode)
                {
                    var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
                    if (dto == null)
                        return AuthResult.Fail("Некорректный ответ сервера");

                    ApplySession(dto);
                    Console.WriteLine($"[Auth] {(endpoint.Contains("register") ? "Registered" : "Logged in")}: {dto.Username}");
                    return AuthResult.Ok();
                }

                return AuthResult.Fail(await ExtractErrorMessageAsync(response));
            }
            catch (TaskCanceledException)
            {
                return AuthResult.Fail("Сервер не отвечает. Он мог уснуть на бесплатном хостинге — подожди минуту и попробуй снова.");
            }
            catch (HttpRequestException)
            {
                return AuthResult.Fail("Не удалось подключиться к серверу. Сервер мог уснуть — подожди минуту и повтори.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Error: {ex.Message}");
                return AuthResult.Fail("Ошибка соединения с сервером");
            }
        }

        private void ApplySession(AuthResponseDto dto)
        {
            CurrentUserId = dto.UserId;
            CurrentUsername = dto.Username;
            CurrentAccessToken = dto.AccessToken;
            IsAuthenticated = true;
            SaveToken(dto.RefreshToken);

            // Запоминаем ник для автозаполнения в окне входа
            ConfigService.Instance.Data.Settings.LastRevenantUsername = dto.Username;
            _ = ConfigService.Instance.SaveAsync();

            AuthStateChanged?.Invoke();
        }

        /// <summary>Запрос с Bearer-токеном</summary>
        private HttpRequestMessage AuthorizedRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(CurrentAccessToken))
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CurrentAccessToken);
            return request;
        }

        /// <summary>Продление сессии по сохранённому refresh-токену</summary>
        private async Task<bool> RefreshSessionAsync()
        {
            var token = LoadToken();
            if (string.IsNullOrEmpty(token))
                return false;

            try
            {
                var response = await _http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token });

                if (!response.IsSuccessStatusCode)
                {
                    ClearToken();
                    return false;
                }

                var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
                if (dto == null) return false;

                ApplySession(dto);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Refresh failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<AuthErrorDto>(JsonOptions);
                if (!string.IsNullOrWhiteSpace(error?.Message))
                    return error.Message;
            }
            catch { /* игнорируем — вернём дефолт */ }

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Неверный никнейм или пароль",
                System.Net.HttpStatusCode.Conflict => "Этот никнейм уже занят",
                System.Net.HttpStatusCode.TooManyRequests => "Слишком много попыток. Подожди минуту.",
                _ => "Ошибка сервера. Попробуй позже."
            };
        }

        // ===== Хранение refresh-токена (DPAPI: расшифровать может только этот пользователь Windows) =====

        private static void SaveToken(string refreshToken)
        {
            try
            {
                var plain = Encoding.UTF8.GetBytes(refreshToken);
                var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(PathService.AuthTokenFile, encrypted);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Save token failed: {ex.Message}");
            }
        }

        private static string? LoadToken()
        {
            try
            {
                if (!File.Exists(PathService.AuthTokenFile))
                    return null;

                var encrypted = File.ReadAllBytes(PathService.AuthTokenFile);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var token = Encoding.UTF8.GetString(plain);
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Load token failed: {ex.Message}");
                return null;
            }
        }

        private static void ClearToken()
        {
            try
            {
                if (File.Exists(PathService.AuthTokenFile))
                    File.Delete(PathService.AuthTokenFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Clear token failed: {ex.Message}");
            }
        }
    }
}
