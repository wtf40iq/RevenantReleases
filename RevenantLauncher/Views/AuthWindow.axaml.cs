using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using RevenantLauncher.ViewModels;

namespace RevenantLauncher.Views
{
    public partial class AuthWindow : Window
    {
        private readonly AuthViewModel _viewModel = new();
        private bool _authSucceeded;

        /// <summary>Вызывается, когда пользователь успешно вошёл или зарегистрировался</summary>
        public event Action? AuthCompleted;

        /// <summary>True, если окно закрыто после успешной авторизации</summary>
        public bool Authenticated => _authSucceeded;

        public AuthWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;

            _viewModel.AuthSucceeded += OnAuthSucceeded;
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AuthViewModel.CurrentTab))
                    ApplyTabStyles();
            };

            Opened += (_, _) => ApplyTabStyles();
        }

        private void OnAuthSucceeded()
        {
            _authSucceeded = true;
            AuthCompleted?.Invoke();
            Close();
        }

        // ===== Вкладки =====

        private void ApplyTabStyles()
        {
            var loginBg = this.FindControl<Border>("LoginTabBg");
            var registerBg = this.FindControl<Border>("RegisterTabBg");
            var loginText = this.FindControl<Button>("LoginTabBtn")?.Content as TextBlock
                            ?? FindTextInside("LoginTabBtn");
            var registerText = this.FindControl<Button>("RegisterTabBtn")?.Content as TextBlock
                               ?? FindTextInside("RegisterTabBtn");

            var activeBrush = new SolidColorBrush(Color.Parse("#7C4DFF"));
            var inactiveBrush = new SolidColorBrush(Color.Parse("#00000000"));

            bool isLogin = _viewModel.IsLoginTab;

            if (loginBg != null) loginBg.Background = isLogin ? activeBrush : inactiveBrush;
            if (registerBg != null) registerBg.Background = isLogin ? inactiveBrush : activeBrush;
            if (loginText != null) loginText.Foreground = new SolidColorBrush(Colors.White);
            if (registerText != null) registerText.Foreground = new SolidColorBrush(Colors.White);
        }

        private TextBlock? FindTextInside(string buttonName)
        {
            var btn = this.FindControl<Button>(buttonName);
            foreach (var child in btn?.GetVisualDescendants() ?? System.Linq.Enumerable.Empty<Avalonia.Visual>())
            {
                if (child is TextBlock tb) return tb;
            }
            return null;
        }

        private void TabLogin_Click(object? sender, RoutedEventArgs e)
        {
            _viewModel.SetTab(AuthTab.Login);
        }

        private void TabRegister_Click(object? sender, RoutedEventArgs e)
        {
            _viewModel.SetTab(AuthTab.Register);
        }

        // ===== Отправка =====

        private async void Submit_Click(object? sender, RoutedEventArgs e)
        {
            await _viewModel.SubmitAsync();
        }

        private async void Field_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await _viewModel.SubmitAsync();
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DragArea_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }
    }
}
