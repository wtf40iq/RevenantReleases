using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RevenantLauncher.Models;
using RevenantLauncher.Services;

namespace RevenantLauncher.Views
{
    public partial class FriendsView : UserControl
    {
        private ViewModels.FriendsViewModel ViewModel => (ViewModels.FriendsViewModel)DataContext!;

        public FriendsView()
        {
            InitializeComponent();
        }

        private void Search_Click(object? sender, RoutedEventArgs e)
        {
            _ = ViewModel.SearchAsync();
        }

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                _ = ViewModel.SearchAsync();
        }

        private void Result_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayerSearchResult result)
                _ = ViewModel.ShowProfileAsync(result.Username);
        }

        private void ToggleFriend_Click(object? sender, RoutedEventArgs e)
        {
            _ = ViewModel.ToggleFriendAsync();
        }

        private void RemoveFriend_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayerProfile friend)
                _ = ViewModel.RemoveFriendAsync(friend);
            e.Handled = true;
        }

        private void OpenChat_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlayerProfile friend)
                _ = ViewModel.OpenChatAsync(friend);
            e.Handled = true;
        }

        private void CloseChat_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.CloseChat();
        }

        private void SendMessage_Click(object? sender, RoutedEventArgs e)
        {
            _ = ViewModel.SendMessageAsync();
        }

        private void ChatBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                _ = ViewModel.SendMessageAsync();
            }
        }

        /// <summary>Клик по карточке друга открывает его профиль (кроме клика по кнопке удаления)</summary>
        private void FriendCard_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not PlayerProfile friend)
                return;

            // Если нажатие началось на кнопке (удаление) — не открываем профиль
            var source = e.Source as Visual;
            while (source != null)
            {
                if (source is Button) return;
                source = source.Parent as Visual;
            }

            _ = ViewModel.ShowProfileAsync(friend.Username);
        }

        private void PlayVersion_Click(object? sender, RoutedEventArgs e)
        {
            ViewModel.PlayProfileVersion();
        }

        private void RefreshFriends_Click(object? sender, RoutedEventArgs e)
        {
            SoundService.Instance.PlayClick();
            _ = ViewModel.LoadFriendsAsync();
            _ = ViewModel.LoadRequestsAsync();
        }

        private void AcceptRequest_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FriendRequest request)
                _ = ViewModel.AcceptRequestAsync(request);
        }

        private void DeclineRequest_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FriendRequest request)
                _ = ViewModel.DeclineRequestAsync(request);
        }

        private void DeclineProfileRequest_Click(object? sender, RoutedEventArgs e)
        {
            _ = ViewModel.DeclineProfileRequestAsync();
        }
    }
}
