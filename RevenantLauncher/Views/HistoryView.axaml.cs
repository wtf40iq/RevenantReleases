using Avalonia.Controls;
using Avalonia.Interactivity;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class HistoryView : UserControl
    {
        private HistoryViewModel ViewModel => (HistoryViewModel)DataContext!;

        public HistoryView()
        {
            InitializeComponent();
        }

        private void PeriodAll_Click(object? sender, RoutedEventArgs e) => ViewModel.SetPeriod(HistoryPeriod.AllTime);
        private void PeriodToday_Click(object? sender, RoutedEventArgs e) => ViewModel.SetPeriod(HistoryPeriod.Today);
        private void PeriodWeek_Click(object? sender, RoutedEventArgs e) => ViewModel.SetPeriod(HistoryPeriod.Week);
        private void PeriodMonth_Click(object? sender, RoutedEventArgs e) => ViewModel.SetPeriod(HistoryPeriod.Month);

        private void StatusAll_Click(object? sender, RoutedEventArgs e) => ViewModel.SetStatusFilter(HistoryStatusFilter.All);
        private void StatusCompleted_Click(object? sender, RoutedEventArgs e) => ViewModel.SetStatusFilter(HistoryStatusFilter.Completed);
        private void StatusFailed_Click(object? sender, RoutedEventArgs e) => ViewModel.SetStatusFilter(HistoryStatusFilter.Failed);

        private void SessionCard_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SessionItem item)
                ViewModel.ToggleExpand(item);
        }

        private async void ClearHistory_Click(object? sender, RoutedEventArgs e)
        {
            await ViewModel.ClearHistoryAsync();
        }
    }
}