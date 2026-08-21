using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class HistoryService
    {
        private static readonly Lazy<HistoryService> _instance = new(() => new HistoryService());
        public static HistoryService Instance => _instance.Value;

        private const int MaxSessions = 500;

        public ObservableCollection<PlaySession> Sessions { get; } = new();

        private PlaySession? _currentSession;

        // Активные сессии (может быть запущено несколько копий игры одновременно)
        private readonly List<PlaySession> _activeSessions = new();

        private HistoryService() { }

        public void Load()
        {
            Sessions.Clear();
            var stored = ConfigService.Instance.Data.PlaySessions
                .OrderByDescending(s => s.StartedAt)
                .Take(MaxSessions)
                .ToList();

            foreach (var s in stored)
                Sessions.Add(s);

            Console.WriteLine($"[HistoryService] Loaded {Sessions.Count} sessions");
        }

        public PlaySession StartSession(GameVersion version, AccountModel account)
        {
            var session = new PlaySession
            {
                VersionId = version.Id,
                VersionDisplayName = version.DisplayName,
                LoaderType = version.Type.ToString(),
                AccountUsername = account.Username,
                StartedAt = DateTime.Now,
                Status = SessionStatus.Completed
            };

            _currentSession = session;
            lock (_activeSessions) _activeSessions.Add(session);
            Console.WriteLine($"[HistoryService] Started session: {version.DisplayName}");
            return session;
        }

        /// <summary>Завершает конкретную сессию (для нескольких одновременных запусков)</summary>
        public async Task EndSessionAsync(PlaySession session, int exitCode)
        {
            lock (_activeSessions)
            {
                if (!_activeSessions.Remove(session)) return;
                if (ReferenceEquals(_currentSession, session)) _currentSession = null;
            }

            session.EndedAt = DateTime.Now;
            session.ExitCode = exitCode;
            session.Status = exitCode == 0 ? SessionStatus.Completed : SessionStatus.Crashed;

            await SaveSessionAsync(session);
        }

        public async Task EndSessionAsync(int exitCode)
        {
            // Берём самую раннюю активную сессию (FIFO)
            PlaySession? session;
            lock (_activeSessions)
                session = _activeSessions.Count > 0 ? _activeSessions[0] : null;

            if (session == null) return;
            await EndSessionAsync(session, exitCode);
        }

        public async Task MarkFailedAsync(string errorMessage)
        {
            // Помечаем последнюю начатую сессию как неудачный запуск
            PlaySession? session;
            lock (_activeSessions)
            {
                session = _activeSessions.Count > 0 ? _activeSessions[^1] : null;
                if (session != null) _activeSessions.Remove(session);
                if (ReferenceEquals(_currentSession, session)) _currentSession = null;
            }

            if (session == null) return;

            session.EndedAt = DateTime.Now;
            session.Status = SessionStatus.FailedToStart;
            session.ErrorMessage = errorMessage;

            await SaveSessionAsync(session);
        }

        private async Task SaveSessionAsync(PlaySession session)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Sessions.Insert(0, session);
                while (Sessions.Count > MaxSessions)
                    Sessions.RemoveAt(Sessions.Count - 1);

                // Синхронизируем с ConfigService.Data
                ConfigService.Instance.Data.PlaySessions = Sessions.ToList();
            });

            Console.WriteLine($"[HistoryService] Saving {Sessions.Count} sessions to config");
            await ConfigService.Instance.SaveAsync();

            // Отправляем сессию на сервер для статистики в профиле/друзьях
            // (fire-and-forget: при отсутствии сети просто молча пропускаем)
            _ = AuthService.Instance.ReportSessionAsync(session);
        }

        public async Task ClearAllAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Sessions.Clear();
                ConfigService.Instance.Data.PlaySessions.Clear();
            });
            await ConfigService.Instance.SaveAsync();
        }

        // ===== Статистика =====

        public int TotalSessions => Sessions.Count;

        public TimeSpan TotalPlayTime => TimeSpan.FromSeconds(
            Sessions.Where(s => s.EndedAt.HasValue).Sum(s => s.Duration.TotalSeconds));

        public TimeSpan LongestSession => Sessions.Count == 0
            ? TimeSpan.Zero
            : Sessions.Max(s => s.Duration);

        public TimeSpan AverageSessionDuration
        {
            get
            {
                var completed = Sessions.Where(s => s.EndedAt.HasValue).ToList();
                if (completed.Count == 0) return TimeSpan.Zero;
                var avgSec = completed.Average(s => s.Duration.TotalSeconds);
                return TimeSpan.FromSeconds(avgSec);
            }
        }

        public string FavoriteVersion
        {
            get
            {
                if (Sessions.Count == 0) return "—";
                var grouped = Sessions
                    .GroupBy(s => s.VersionDisplayName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                if (grouped == null) return "—";
                return $"{grouped.Key} ({grouped.Count()} раз)";
            }
        }

        public DateTime? FirstSessionDate =>
            Sessions.Count == 0 ? null : Sessions.Min(s => s.StartedAt);

        public List<DayActivity> GetActivityLast30Days()
        {
            var result = new List<DayActivity>();
            var today = DateTime.Today;

            for (int i = 29; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var dayEnd = day.AddDays(1);

                var hoursThisDay = Sessions
                    .Where(s => s.StartedAt >= day && s.StartedAt < dayEnd)
                    .Sum(s => s.Duration.TotalHours);

                result.Add(new DayActivity
                {
                    Date = day,
                    Hours = hoursThisDay
                });
            }

            return result;
        }
    }

    public class DayActivity
    {
        public DateTime Date { get; set; }
        public double Hours { get; set; }
        public string DayLabel => Date.ToString("d.MM");
        public bool IsToday => Date == DateTime.Today;
    }
}