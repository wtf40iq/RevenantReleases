using System;

namespace RevenantLauncher.Models
{
    public enum SessionStatus
    {
        Completed,      // Нормально закрылась (exit code 0)
        Crashed,        // Упала с ошибкой
        FailedToStart   // Не удалось запустить
    }

    public class PlaySession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string VersionId { get; set; } = string.Empty;
        public string VersionDisplayName { get; set; } = string.Empty;
        public string LoaderType { get; set; } = "Vanilla";  // Vanilla / Forge / Fabric / Quilt / NeoForge
        public string AccountUsername { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; } = DateTime.Now;
        public DateTime? EndedAt { get; set; }
        public SessionStatus Status { get; set; } = SessionStatus.Completed;
        public int? ExitCode { get; set; }
        public string? ErrorMessage { get; set; }

        // Computed
        public TimeSpan Duration => (EndedAt ?? DateTime.Now) - StartedAt;

        public string DurationDisplay
        {
            get
            {
                var d = Duration;
                if (d.TotalMinutes < 1) return "меньше минуты";
                if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} мин";
                return $"{(int)d.TotalHours} ч {d.Minutes} мин";
            }
        }

        public string StartedAtDisplay => StartedAt.ToString("dd MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("ru-RU"));
        public string StartedAtShort => StartedAt.ToString("dd.MM.yyyy HH:mm");

        public string StatusLabel => Status switch
        {
            SessionStatus.Completed => "Завершено",
            SessionStatus.Crashed => "Упало",
            SessionStatus.FailedToStart => "Не запустилось",
            _ => "?"
        };

        public string StatusColor => Status switch
        {
            SessionStatus.Completed => "#66BB6A",
            SessionStatus.Crashed => "#FF6666",
            SessionStatus.FailedToStart => "#FFB74D",
            _ => "#888888"
        };

        public string LoaderColor => LoaderType?.ToLower() switch
        {
            "forge" => "#FF9800",
            "fabric" => "#B388FF",
            "quilt" => "#DD88FF",
            "neoforge" => "#FF6B6B",
            _ => "#66BB6A"
        };
    }
}