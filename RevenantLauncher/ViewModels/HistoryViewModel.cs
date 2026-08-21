using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.ViewModels
{
    public enum HistoryPeriod
    {
        AllTime,
        Today,
        Week,
        Month
    }

    public enum HistoryStatusFilter
    {
        All,
        Completed,
        Failed
    }

    public class HistoryViewModel : ViewModelBase
    {
        private string _searchText = "";
        private HistoryPeriod _period = HistoryPeriod.AllTime;
        private HistoryStatusFilter _statusFilter = HistoryStatusFilter.All;
        private double _maxDayHours = 1;

        public ObservableCollection<SessionItem> DisplayedSessions { get; } = new();
        public ObservableCollection<DayActivityItem> Activity { get; } = new();

        public HistoryViewModel()
        {
            RefreshList();
            RefreshActivity();

            HistoryService.Instance.Sessions.CollectionChanged += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshList();
                    RefreshActivity();
                    RefreshStats();
                });
            };
        }

        // ===== Фильтры =====
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RefreshList();
            }
        }

        public HistoryPeriod Period
        {
            get => _period;
            set
            {
                if (SetProperty(ref _period, value))
                {
                    OnPropertyChanged(nameof(PeriodAllActive));
                    OnPropertyChanged(nameof(PeriodTodayActive));
                    OnPropertyChanged(nameof(PeriodWeekActive));
                    OnPropertyChanged(nameof(PeriodMonthActive));
                    RefreshList();
                }
            }
        }

        public bool PeriodAllActive => Period == HistoryPeriod.AllTime;
        public bool PeriodTodayActive => Period == HistoryPeriod.Today;
        public bool PeriodWeekActive => Period == HistoryPeriod.Week;
        public bool PeriodMonthActive => Period == HistoryPeriod.Month;

        public HistoryStatusFilter StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value))
                {
                    OnPropertyChanged(nameof(StatusAllActive));
                    OnPropertyChanged(nameof(StatusCompletedActive));
                    OnPropertyChanged(nameof(StatusFailedActive));
                    RefreshList();
                }
            }
        }

        public bool StatusAllActive => StatusFilter == HistoryStatusFilter.All;
        public bool StatusCompletedActive => StatusFilter == HistoryStatusFilter.Completed;
        public bool StatusFailedActive => StatusFilter == HistoryStatusFilter.Failed;

        public void SetPeriod(HistoryPeriod p) => Period = p;
        public void SetStatusFilter(HistoryStatusFilter f) => StatusFilter = f;

        // ===== Статистика =====
        public int TotalSessions => HistoryService.Instance.TotalSessions;

        public string TotalPlayTimeDisplay
        {
            get
            {
                var t = HistoryService.Instance.TotalPlayTime;
                if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} мин";
                return $"{(int)t.TotalHours} ч {t.Minutes} мин";
            }
        }

        public string LongestSessionDisplay
        {
            get
            {
                var t = HistoryService.Instance.LongestSession;
                if (t == TimeSpan.Zero) return "—";
                if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} мин";
                return $"{(int)t.TotalHours} ч {t.Minutes} мин";
            }
        }

        public string AverageSessionDisplay
        {
            get
            {
                var t = HistoryService.Instance.AverageSessionDuration;
                if (t == TimeSpan.Zero) return "—";
                if (t.TotalHours < 1) return $"{(int)t.TotalMinutes} мин";
                return $"{(int)t.TotalHours} ч {t.Minutes} мин";
            }
        }

        public string FavoriteVersion => HistoryService.Instance.FavoriteVersion;

        public string FirstSessionDisplay
        {
            get
            {
                var d = HistoryService.Instance.FirstSessionDate;
                if (d == null) return "—";
                var days = (DateTime.Now - d.Value).TotalDays;
                if (days < 1) return "сегодня";
                if (days < 7) return $"{(int)days} дн. назад";
                if (days < 30) return $"{(int)(days / 7)} нед. назад";
                if (days < 365) return $"{(int)(days / 30)} мес. назад";
                return $"{(int)(days / 365)} г. назад";
            }
        }

        public bool HasSessions => HistoryService.Instance.TotalSessions > 0;

        public double MaxDayHours
        {
            get => _maxDayHours;
            set => SetProperty(ref _maxDayHours, value);
        }

        // ===== Действия =====
        public async Task ClearHistoryAsync()
        {
            await HistoryService.Instance.ClearAllAsync();
            ToastService.Instance.ShowInfo("История очищена", "Все записи удалены");
        }

        public void ToggleExpand(SessionItem item)
        {
            item.IsExpanded = !item.IsExpanded;
        }

        // ===== Обновление данных =====
        private void RefreshList()
        {
            IEnumerable<PlaySession> source = HistoryService.Instance.Sessions;

            // Фильтр по периоду
            var now = DateTime.Now;
            source = Period switch
            {
                HistoryPeriod.Today => source.Where(s => s.StartedAt.Date == now.Date),
                HistoryPeriod.Week => source.Where(s => s.StartedAt >= now.AddDays(-7)),
                HistoryPeriod.Month => source.Where(s => s.StartedAt >= now.AddDays(-30)),
                _ => source
            };

            // Фильтр по статусу
            source = StatusFilter switch
            {
                HistoryStatusFilter.Completed => source.Where(s => s.Status == SessionStatus.Completed),
                HistoryStatusFilter.Failed => source.Where(s =>
                    s.Status == SessionStatus.Crashed || s.Status == SessionStatus.FailedToStart),
                _ => source
            };

            // Поиск
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.Trim();
                source = source.Where(s =>
                    s.VersionDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    s.AccountUsername.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            DisplayedSessions.Clear();
            foreach (var s in source)
                DisplayedSessions.Add(new SessionItem(s));
        }

        private void RefreshActivity()
        {
            Activity.Clear();
            var data = HistoryService.Instance.GetActivityLast30Days();
            var max = Math.Max(1, data.Max(d => d.Hours));

            foreach (var d in data)
            {
                Activity.Add(new DayActivityItem
                {
                    Date = d.Date,
                    Hours = d.Hours,
                    DayLabel = d.DayLabel,
                    IsToday = d.IsToday,
                    BarHeight = 80 * (d.Hours / max) // максимум 80px
                });
            }
        }

        private void RefreshStats()
        {
            OnPropertyChanged(nameof(TotalSessions));
            OnPropertyChanged(nameof(TotalPlayTimeDisplay));
            OnPropertyChanged(nameof(LongestSessionDisplay));
            OnPropertyChanged(nameof(AverageSessionDisplay));
            OnPropertyChanged(nameof(FavoriteVersion));
            OnPropertyChanged(nameof(FirstSessionDisplay));
            OnPropertyChanged(nameof(HasSessions));
        }
    }

    /// <summary>Обёртка над PlaySession с состоянием IsExpanded</summary>
    public class SessionItem : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public PlaySession Session { get; }

        public SessionItem(PlaySession session)
        {
            Session = session;
        }

        public string VersionDisplayName => Session.VersionDisplayName;
        public string LoaderType => Session.LoaderType;
        public string LoaderColor => Session.LoaderColor;
        public string AccountUsername => Session.AccountUsername;
        public string StartedAtDisplay => Session.StartedAtDisplay;
        public string StartedAtShort => Session.StartedAtShort;
        public string DurationDisplay => Session.DurationDisplay;
        public string StatusLabel => Session.StatusLabel;
        public string StatusColor => Session.StatusColor;
        public int? ExitCode => Session.ExitCode;
        public string? ErrorMessage => Session.ErrorMessage;
        public string VersionId => Session.VersionId;

        public bool IsCompleted => Session.Status == SessionStatus.Completed;
        public bool IsCrashed => Session.Status == SessionStatus.Crashed;
        public bool IsFailed => Session.Status == SessionStatus.FailedToStart;

        public string EndedAtDisplay => Session.EndedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        public bool HasError => !string.IsNullOrEmpty(Session.ErrorMessage);

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DayActivityItem : ViewModelBase
    {
        public DateTime Date { get; set; }
        public double Hours { get; set; }
        public string DayLabel { get; set; } = "";
        public bool IsToday { get; set; }
        public double BarHeight { get; set; }

        public string HoursDisplay
        {
            get
            {
                if (Hours < 0.01) return "0";
                if (Hours < 1) return $"{(int)(Hours * 60)}м";
                return $"{Hours:F1}ч";
            }
        }
    }
}