using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using RevenantAuthServer.Data;
using RevenantAuthServer.Models;
using RevenantAuthServer.Services;

// Render free: исчерпан лимит inotify — отключаем слежку за конфиг-файлами ДО создания builder'а
Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");

var builder = WebApplication.CreateBuilder(args);

// ===== Конфигурация =====
// Секрет JWT берётся из переменной окружения JWT_SECRET (обязательно задать на Render!).
// Без него сервер НЕ запускается: публично известный dev-секрет позволил бы подделывать токены.
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "JWT_SECRET не задан или короче 32 символов. Задай переменную окружения JWT_SECRET " +
        "(сгенерировать: openssl rand -base64 48) — сервер без неё не запустится.");
}

const string jwtIssuer = "revenant-auth-server";
const string jwtAudience = "revenant-launcher";

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("DATABASE_URL не задан. Укажи env-переменную со строкой подключения Neon/Postgres.");

// Postgres (Neon/Render) — строка вида postgres://... из DATABASE_URL.
// Всё остальное (по умолчанию SQLite Data Source=data/revenant.db) — локальная разработка.
var isPostgres = connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
    || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
    || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AuthDbContext>(options =>
{
    if (isPostgres)
        options.UseNpgsql(NormalizeConnectionString(connectionString));
    else
        options.UseSqlite(connectionString);
});
builder.Services.AddSingleton(new TokenService(jwtSecret, jwtIssuer, jwtAudience));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// Защита от brute-force: максимум 20 запросов в минуту с одного IP на auth-эндпоинты
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync("{\"message\":\"Слишком много попыток. Подожди минуту.\"}", ct);
    };
    options.AddPolicy<string>("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 20,
            QueueLimit = 0
        }));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ===== База данных =====
if (!isPostgres)
{
    // Для SQLite создаём папку базы заранее, иначе EnsureCreated упадёт
    try
    {
        var sqlitePath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
        if (!string.IsNullOrEmpty(sqlitePath))
        {
            var dir = Path.GetDirectoryName(sqlitePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
    catch { /* некритично — EnsureCreated сам сообщит об ошибке */ }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.EnsureCreated();

    // Лёгкая миграция для уже существующих баз: таблица заявок в друзья.
    // Fresh-базы получают её через EnsureCreated, старые — этой командой.
    db.Database.ExecuteSqlRaw(isPostgres
        ? "CREATE TABLE IF NOT EXISTS friend_requests (id SERIAL PRIMARY KEY, requester_id INTEGER NOT NULL, addressee_id INTEGER NOT NULL, created_at TIMESTAMPTZ NOT NULL)"
        : "CREATE TABLE IF NOT EXISTS friend_requests (id INTEGER PRIMARY KEY AUTOINCREMENT, requester_id INTEGER NOT NULL, addressee_id INTEGER NOT NULL, created_at TEXT NOT NULL)");
    db.Database.ExecuteSqlRaw(isPostgres
        ? "CREATE TABLE IF NOT EXISTS presence (user_id INTEGER PRIMARY KEY, status TEXT NOT NULL, version TEXT, updated_at TIMESTAMPTZ NOT NULL)"
        : "CREATE TABLE IF NOT EXISTS presence (user_id INTEGER PRIMARY KEY, status TEXT NOT NULL, version TEXT, updated_at TEXT NOT NULL)");
    db.Database.ExecuteSqlRaw(isPostgres
        ? "CREATE TABLE IF NOT EXISTS notifications (id BIGSERIAL PRIMARY KEY, user_id INTEGER NOT NULL, type TEXT NOT NULL, actor_id INTEGER NULL, text TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL, read_at TIMESTAMPTZ NULL)"
        : "CREATE TABLE IF NOT EXISTS notifications (id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER NOT NULL, type TEXT NOT NULL, actor_id INTEGER NULL, text TEXT NOT NULL, created_at TEXT NOT NULL, read_at TEXT NULL)");
    db.Database.ExecuteSqlRaw(isPostgres
        ? "CREATE TABLE IF NOT EXISTS chat_messages (id BIGSERIAL PRIMARY KEY, sender_id INTEGER NOT NULL, recipient_id INTEGER NOT NULL, text TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL)"
        : "CREATE TABLE IF NOT EXISTS chat_messages (id INTEGER PRIMARY KEY AUTOINCREMENT, sender_id INTEGER NOT NULL, recipient_id INTEGER NOT NULL, text TEXT NOT NULL, created_at TEXT NOT NULL)");
}
Console.WriteLine("[Auth] Database ready");

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ===== Эндпоинты =====

app.MapGet("/", () => Results.Ok(new { name = "Revenant Auth Server", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

// Регистрация
auth.MapPost("/register", async (RegisterRequest req, HttpContext http, AuthDbContext db, TokenService tokens) =>
{
    var error = ValidateCredentials(req.Username, req.Password);
    if (error != null)
        return Results.BadRequest(new { message = error });

    var username = req.Username!.Trim();

    if (await db.Users.AnyAsync(u => u.Username == username))
        return Results.Conflict(new { message = "Этот никнейм уже занят" });

    var (hash, salt) = PasswordHasher.Hash(req.Password!);

    var user = new User
    {
        Username = username,
        PasswordHash = hash,
        PasswordSalt = salt,
        RefreshToken = TokenService.CreateRefreshToken(),
        RefreshTokenExpiry = DateTime.UtcNow.Add(TokenService.RefreshTokenLifetime),
        LastLoginAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    db.LoginHistory.Add(new LoginHistory
    {
        UserId = user.Id,
        Ip = GetClientIp(http),
        At = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    Console.WriteLine($"[Auth] Registered: {username} (id={user.Id})");
    return Results.Ok(new AuthResponse(tokens.CreateAccessToken(user), user.RefreshToken!, user.Id, user.Username));
});

// Вход
auth.MapPost("/login", async (LoginRequest req, HttpContext http, AuthDbContext db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new { message = "Введи никнейм и пароль" });

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username.Trim());

    if (user == null || !PasswordHasher.Verify(req.Password!, user.PasswordHash, user.PasswordSalt))
        return UnauthorizedJson("Неверный никнейм или пароль");

    // Ротация refresh-токена при каждом входе
    user.RefreshToken = TokenService.CreateRefreshToken();
    user.RefreshTokenExpiry = DateTime.UtcNow.Add(TokenService.RefreshTokenLifetime);
    user.LastLoginAt = DateTime.UtcNow;
    db.LoginHistory.Add(new LoginHistory
    {
        UserId = user.Id,
        Ip = GetClientIp(http),
        At = DateTime.UtcNow
    });
    await db.SaveChangesAsync();

    Console.WriteLine($"[Auth] Login: {user.Username}");
    return Results.Ok(new AuthResponse(tokens.CreateAccessToken(user), user.RefreshToken!, user.Id, user.Username));
});

// Продление сессии по refresh-токену (ротация токена)
auth.MapPost("/refresh", async (RefreshRequest req, AuthDbContext db, TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(req.RefreshToken))
        return UnauthorizedJson("Нет refresh-токена");

    var user = await db.Users.FirstOrDefaultAsync(u => u.RefreshToken == req.RefreshToken);

    if (user == null || user.RefreshTokenExpiry < DateTime.UtcNow)
        return UnauthorizedJson("Сессия истекла");

    user.RefreshToken = TokenService.CreateRefreshToken();
    user.RefreshTokenExpiry = DateTime.UtcNow.Add(TokenService.RefreshTokenLifetime);
    await db.SaveChangesAsync();

    return Results.Ok(new AuthResponse(tokens.CreateAccessToken(user), user.RefreshToken!, user.Id, user.Username));
});

// Выход (аннулирование refresh-токена)
auth.MapPost("/logout", async (RefreshRequest req, AuthDbContext db) =>
{
    if (!string.IsNullOrWhiteSpace(req.RefreshToken))
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.RefreshToken == req.RefreshToken);
        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiry = DateTime.MinValue;
            await db.SaveChangesAsync();
            Console.WriteLine($"[Auth] Logout: {user.Username}");
        }
    }
    return Results.Ok(new { message = "ok" });
});

// Текущий пользователь (по access-токену)
auth.MapGet("/me", async (ClaimsPrincipal principal, HttpContext http, AuthDbContext db) =>
{
    var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? principal.FindFirst("sub")?.Value;

    if (!int.TryParse(idClaim, out var id))
        return UnauthorizedJson("Сессия недействительна");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user == null)
        return UnauthorizedJson("Сессия недействительна");

    // Последние входы (как в Cloudflare-воркере, чтобы бэкенды вели себя одинаково)
    var history = await db.LoginHistory
        .Where(h => h.UserId == id)
        .OrderByDescending(h => h.At)
        .Take(5)
        .Select(h => new LoginHistoryDto(h.Ip ?? "", h.At))
        .ToListAsync();

    return Results.Ok(new MeResponse(
        user.Id, user.Username, user.CreatedAt, user.LastLoginAt,
        GetClientIp(http), history));
}).RequireAuthorization();

// Смена пароля (нужен валидный access-токен)
auth.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? principal.FindFirst("sub")?.Value;

    if (!int.TryParse(idClaim, out var id))
        return UnauthorizedJson("Сессия недействительна");

    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user == null)
        return UnauthorizedJson("Сессия недействительна");

    if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
        return Results.BadRequest(new { message = "Заполни оба поля" });

    if (!PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash, user.PasswordSalt))
        return Results.BadRequest(new { message = "Неверный текущий пароль" });

    if (req.NewPassword.Length < 6)
        return Results.BadRequest(new { message = "Пароль должен быть минимум 6 символов" });
    if (req.NewPassword.Length > 64)
        return Results.BadRequest(new { message = "Пароль не может быть длиннее 64 символов" });

    var (hash, salt) = PasswordHasher.Hash(req.NewPassword);
    user.PasswordHash = hash;
    user.PasswordSalt = salt;
    await db.SaveChangesAsync();

    Console.WriteLine($"[Auth] Password changed: {user.Username}");
    return Results.Ok(new { message = "Пароль изменён" });
}).RequireAuthorization();

// ===== Социальные эндпоинты: сессии, поиск игроков, профили, друзья =====
// Все требуют авторизации и ограничены rate limiter'ом, как auth-эндпоинты
var social = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("auth");

// Лаунчер сообщает об окончании игровой сессии (для статистики в профиле)
social.MapPost("/sessions", async (SessionReportRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    if (string.IsNullOrWhiteSpace(req.StartedAt) || !DateTime.TryParse(req.StartedAt, out var started))
        return Results.BadRequest(new { message = "Некорректный startedAt" });

    DateTime? ended = null;
    if (!string.IsNullOrWhiteSpace(req.EndedAt) && DateTime.TryParse(req.EndedAt, out var parsedEnded))
        ended = parsedEnded;

    var durationSec = Math.Max(0, (long)((ended ?? started) - started).TotalSeconds);

    db.PlaySessions.Add(new PlaySessionRecord
    {
        UserId = me.Value,
        StartedAt = started,
        EndedAt = ended,
        DurationSec = durationSec,
        VersionId = req.VersionId,
        VersionDisplay = req.VersionDisplay,
        Loader = req.Loader,
        Status = req.Status
    });
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "ok" });
});

// Присутствие лаунчера: запись свежа 90 секунд.
social.MapPost("/presence", async (PresenceRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    if (req.Status is not ("online" or "playing")) return Results.BadRequest(new { message = "Некорректный статус" });
    var row = await db.Presence.FirstOrDefaultAsync(p => p.UserId == me.Value);
    if (row == null) db.Presence.Add(new PresenceRecord { UserId = me.Value, Status = req.Status!, Version = req.Version, UpdatedAt = DateTime.UtcNow });
    else { row.Status = req.Status!; row.Version = req.Version; row.UpdatedAt = DateTime.UtcNow; }
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "ok" });
});

social.MapDelete("/presence", async (ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    var row = await db.Presence.FirstOrDefaultAsync(p => p.UserId == me.Value);
    if (row != null) { db.Presence.Remove(row); await db.SaveChangesAsync(); }
    return Results.Ok(new { message = "ok" });
});

// Уведомления: новые заявки, принятые заявки и сообщения.
social.MapGet("/notifications", async (HttpRequest http, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    var unread = string.Equals(http.Query["unread"], "true", StringComparison.OrdinalIgnoreCase);
    var query = db.Notifications.Where(n => n.UserId == me.Value);
    if (unread) query = query.Where(n => n.ReadAt == null);
    var rows = await query.OrderByDescending(n => n.Id).Take(50).ToListAsync();
    var actorIds = rows.Where(n => n.ActorId.HasValue).Select(n => n.ActorId!.Value).Distinct().ToList();
    var actors = await db.Users.Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username);
    return Results.Ok(rows.Select(n => new SocialNotificationResponse(n.Id, n.Type,
        n.ActorId.HasValue && actors.TryGetValue(n.ActorId.Value, out var name) ? name : null,
        n.Text, n.CreatedAt, n.ReadAt != null)));
});

social.MapPost("/notifications/read", async (NotificationReadRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    var rows = req.Id > 0
        ? await db.Notifications.Where(n => n.UserId == me.Value && n.Id == req.Id).ToListAsync()
        : await db.Notifications.Where(n => n.UserId == me.Value && n.ReadAt == null).ToListAsync();
    foreach (var row in rows) row.ReadAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "ok" });
});

// Чат доступен только между принятыми друзьями.
social.MapGet("/chat/{username}", async (string username, long? afterId, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });
    if (!await db.Friendships.AnyAsync(f => f.UserId == me.Value && f.FriendId == target.Id)) return Results.Json(new { message = "Чат доступен только друзьям" }, statusCode: 403);
    var minId = afterId.GetValueOrDefault();
    var messages = await db.ChatMessages.Where(m => ((m.SenderId == me.Value && m.RecipientId == target.Id) || (m.SenderId == target.Id && m.RecipientId == me.Value)) && m.Id > minId).OrderBy(m => m.Id).Take(100).ToListAsync();
    var meUser = await db.Users.FirstAsync(u => u.Id == me.Value);
    var result = messages.Select(m => new ChatMessageResponse(m.Id,
        m.SenderId == me.Value ? meUser.Username : target.Username,
        m.SenderId == me.Value ? target.Username : meUser.Username,
        m.Text, m.CreatedAt, m.SenderId == me.Value));
    var unread = await db.Notifications.Where(n => n.UserId == me.Value && n.Type == "message" && n.ActorId == target.Id && n.ReadAt == null).ToListAsync();
    foreach (var n in unread) n.ReadAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(result);
});

social.MapPost("/chat/{username}", async (string username, SendMessageRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");
    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });
    if (!await db.Friendships.AnyAsync(f => f.UserId == me.Value && f.FriendId == target.Id)) return Results.Json(new { message = "Чат доступен только друзьям" }, statusCode: 403);
    var text = req.Text?.Trim() ?? "";
    if (text.Length == 0) return Results.BadRequest(new { message = "Напиши сообщение" });
    if (text.Length > 1000) return Results.BadRequest(new { message = "Сообщение слишком длинное" });
    var meUser = await db.Users.FirstAsync(u => u.Id == me.Value);
    var message = new ChatMessage { SenderId = me.Value, RecipientId = target.Id, Text = text, CreatedAt = DateTime.UtcNow };
    db.ChatMessages.Add(message);
    db.Notifications.Add(new SocialNotification { UserId = target.Id, Type = "message", ActorId = me.Value, Text = $"{meUser.Username}: {(text.Length > 80 ? text[..80] + "…" : text)}", CreatedAt = message.CreatedAt });
    await db.SaveChangesAsync();
    return Results.Ok(new SendMessageResponse(true, null, new ChatMessageResponse(message.Id, meUser.Username, target.Username, text, message.CreatedAt, true)));
});

// Поиск игроков по префиксу ника
social.MapGet("/users/search", async (string? q, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    q = q?.Trim();
    if (string.IsNullOrEmpty(q)) return Results.BadRequest(new { message = "Укажи запрос q" });

    var results = new List<PlayerSearchResult>();
    var matches = await db.Users
        .Where(u => u.Id != me.Value && u.Username.ToLower().StartsWith(q.ToLower()))
        .OrderBy(u => u.Username)
        .Take(20)
        .ToListAsync();

    foreach (var m in matches)
    {
        var status = await GetFriendStatusAsync(db, me.Value, m.Id);
        results.Add(new PlayerSearchResult(m.Username, status));
    }
    return Results.Ok(results);
});

// Публичный профиль игрока со статистикой (без чувствительных данных)
social.MapGet("/users/{username}", async (string username, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });

    var stats = await BuildStatsAsync(db, target.Id);
    var status = await GetFriendStatusAsync(db, me.Value, target.Id);
    var presence = await GetPresenceAsync(db, target.Id);
    return Results.Ok(new PlayerProfileResponse(target.Username, target.CreatedAt, status, presence.IsOnline, presence.PresenceStatus, presence.CurrentVersion, stats));
});

// Отправить заявку в друзья (или принять встречную)
social.MapPost("/friends", async (FriendAddRequest req, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var username = req.Username?.Trim() ?? "";
    if (string.IsNullOrEmpty(username)) return Results.BadRequest(new { message = "Укажи ник" });

    var meUser = await db.Users.FirstOrDefaultAsync(u => u.Id == me.Value);
    if (meUser != null && string.Equals(meUser.Username, username, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Нельзя добавить себя" });

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });

    var now = DateTime.UtcNow;

    // Уже друзья
    if (await db.Friendships.AnyAsync(f => f.UserId == me.Value && f.FriendId == target.Id))
        return Results.Ok(new { message = "Вы уже друзья", status = "friend" });

    // Моя заявка уже висит
    if (await db.FriendRequests.AnyAsync(r => r.RequesterId == me.Value && r.AddresseeId == target.Id))
        return Results.Ok(new { message = "Заявка уже отправлена", status = "outgoing" });

    // Есть встречная заявка от него — сразу становимся друзьями
    var theirs = await db.FriendRequests.FirstOrDefaultAsync(r => r.RequesterId == target.Id && r.AddresseeId == me.Value);
    if (theirs != null)
    {
        db.FriendRequests.Remove(theirs);
        db.Friendships.Add(new Friendship { UserId = me.Value, FriendId = target.Id, CreatedAt = now });
        db.Friendships.Add(new Friendship { UserId = target.Id, FriendId = me.Value, CreatedAt = now });
        db.Notifications.Add(new SocialNotification { UserId = target.Id, Type = "friend_accepted", ActorId = me.Value, Text = $"{meUser!.Username} принял твою заявку в друзья", CreatedAt = now });
        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Вы теперь друзья!", status = "accepted" });
    }

    db.FriendRequests.Add(new FriendRequest { RequesterId = me.Value, AddresseeId = target.Id, CreatedAt = now });
    db.Notifications.Add(new SocialNotification { UserId = target.Id, Type = "friend_request", ActorId = me.Value, Text = $"{meUser!.Username} хочет добавить тебя в друзья", CreatedAt = now });
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Заявка отправлена", status = "pending" });
});

// Входящие заявки в друзья
social.MapGet("/friends/requests", async (ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var requests = await db.FriendRequests
        .Where(r => r.AddresseeId == me.Value)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new FriendRequestResponse(
            db.Users.Where(u => u.Id == r.RequesterId).Select(u => u.Username).FirstOrDefault() ?? "?",
            r.CreatedAt))
        .ToListAsync();

    return Results.Ok(requests);
});

// Принять заявку в друзья
social.MapPost("/friends/requests/{username}/accept", async (string username, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });

    var req = await db.FriendRequests.FirstOrDefaultAsync(r => r.RequesterId == target.Id && r.AddresseeId == me.Value);
    if (req == null) return Results.NotFound(new { message = "Заявка не найдена" });

    var now = DateTime.UtcNow;
    db.FriendRequests.Remove(req);
    db.Friendships.Add(new Friendship { UserId = me.Value, FriendId = target.Id, CreatedAt = now });
    db.Friendships.Add(new Friendship { UserId = target.Id, FriendId = me.Value, CreatedAt = now });
    db.Notifications.Add(new SocialNotification { UserId = target.Id, Type = "friend_accepted", ActorId = me.Value, Text = $"{(await db.Users.Where(u => u.Id == me.Value).Select(u => u.Username).FirstAsync())} принял твою заявку в друзья", CreatedAt = now });
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "ok" });
});

// Отклонить заявку в друзья (идемпотентно)
social.MapPost("/friends/requests/{username}/decline", async (string username, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });

    var req = await db.FriendRequests.FirstOrDefaultAsync(r => r.RequesterId == target.Id && r.AddresseeId == me.Value);
    if (req != null)
    {
        db.FriendRequests.Remove(req);
        await db.SaveChangesAsync();
    }
    return Results.Ok(new { message = "ok" });
});

// Убрать из друзей (заодно отменяет любую заявку между нами)
social.MapDelete("/friends/{username}", async (string username, ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound(new { message = "Игрок не найден" });

    db.Friendships.RemoveRange(db.Friendships.Where(f =>
        (f.UserId == me.Value && f.FriendId == target.Id) ||
        (f.UserId == target.Id && f.FriendId == me.Value)));

    db.FriendRequests.RemoveRange(db.FriendRequests.Where(r =>
        (r.RequesterId == me.Value && r.AddresseeId == target.Id) ||
        (r.RequesterId == target.Id && r.AddresseeId == me.Value)));

    await db.SaveChangesAsync();
    return Results.Ok(new { message = "ok" });
});

// Список друзей со статистикой и активностью (только принятые)
social.MapGet("/friends", async (ClaimsPrincipal principal, AuthDbContext db) =>
{
    var me = GetUserId(principal);
    if (me == null) return UnauthorizedJson("Сессия недействительна");

    var friendIds = await db.Friendships
        .Where(f => f.UserId == me.Value)
        .Select(f => f.FriendId)
        .ToListAsync();

    var result = new List<PlayerProfileResponse>();
    foreach (var id in friendIds)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (target == null) continue;
        var stats = await BuildStatsAsync(db, id);
        var presence = await GetPresenceAsync(db, id);
        result.Add(new PlayerProfileResponse(target.Username, target.CreatedAt, "friend", presence.IsOnline, presence.PresenceStatus, presence.CurrentVersion, stats));
    }
    return Results.Ok(result);
});

// Render.com задаёт порт через переменную окружения PORT
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
Console.WriteLine($"[Auth] Listening on port {port}");
app.Run($"http://0.0.0.0:{port}");

// 401 с телом { message } — Results.Unauthorized не принимает объект
static IResult UnauthorizedJson(string message)
    => Results.Json(new { message }, statusCode: StatusCodes.Status401Unauthorized);

// IP клиента для истории входов
static string GetClientIp(HttpContext http)
    => http.Connection.RemoteIpAddress?.ToString() ?? "";

// ID текущего пользователя из JWT
static int? GetUserId(ClaimsPrincipal principal)
{
    var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? principal.FindFirst("sub")?.Value;
    return int.TryParse(idClaim, out var id) ? id : null;
}

// Присутствие считается онлайн, если heartbeat был менее 90 секунд назад.
static async Task<PresenceResponse> GetPresenceAsync(AuthDbContext db, int userId)
{
    var row = await db.Presence.FirstOrDefaultAsync(p => p.UserId == userId);
    if (row == null) return new PresenceResponse(false, "offline", null, null);
    var online = DateTime.UtcNow - row.UpdatedAt < TimeSpan.FromSeconds(90);
    return new PresenceResponse(online, online ? row.Status : "offline", online ? row.Version : null, row.UpdatedAt);
}

// Статус отношений с другим пользователем: none | friend | outgoing | incoming
static async Task<string> GetFriendStatusAsync(AuthDbContext db, int meId, int otherId)
{
    if (await db.Friendships.AnyAsync(f => f.UserId == meId && f.FriendId == otherId))
        return "friend";
    if (await db.FriendRequests.AnyAsync(r => r.RequesterId == meId && r.AddresseeId == otherId))
        return "outgoing";
    if (await db.FriendRequests.AnyAsync(r => r.RequesterId == otherId && r.AddresseeId == meId))
        return "incoming";
    return "none";
}

// Агрегированная игровая статистика пользователя
static async Task<PlayerStatsResponse> BuildStatsAsync(AuthDbContext db, int userId)
{
    var total = await db.PlaySessions
        .Where(s => s.UserId == userId)
        .GroupBy(s => 1)
        .Select(g => new { Count = g.Count(), Sum = g.Sum(s => s.DurationSec) })
        .FirstOrDefaultAsync();

    var fav = await db.PlaySessions
        .Where(s => s.UserId == userId && s.VersionDisplay != null)
        .GroupBy(s => s.VersionDisplay!)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    var last = await db.PlaySessions
        .Where(s => s.UserId == userId)
        .OrderByDescending(s => s.EndedAt ?? s.StartedAt)
        .Select(s => (DateTime?)(s.EndedAt ?? s.StartedAt))
        .FirstOrDefaultAsync();

    return new PlayerStatsResponse(
        total?.Count ?? 0,
        total?.Sum ?? 0,
        fav,
        last == default ? null : last);
}

// Neon отдаёт строку в URI-формате (postgresql://user:pass@host/db),
// а Npgsql принимает только key=value — приводим к нужному виду
static string NormalizeConnectionString(string url)
{
    if (!url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return url;

    var uri = new Uri(url);
    var parts = uri.UserInfo.Split(':', 2);

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(parts[0]),
        Password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "",
        SslMode = SslMode.Require
    };
    if (uri.Port > 0) builder.Port = uri.Port;

    return builder.ConnectionString;
}

// ===== Валидация (те же правила, что в лаунчере) =====
static string? ValidateCredentials(string? username, string? password)
{
    var name = username?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(name))
        return "Введи никнейм";
    if (name.Length < 3 || name.Length > 16)
        return "Ник должен быть от 3 до 16 символов";
    foreach (var ch in name)
    {
        if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            return "Только буквы, цифры и _";
    }

    if (string.IsNullOrWhiteSpace(password))
        return "Введи пароль";
    if (password.Length < 6)
        return "Пароль должен быть минимум 6 символов";
    if (password.Length > 64)
        return "Пароль не может быть длиннее 64 символов";

    return null;
}
