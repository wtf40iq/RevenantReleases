using System;
using System.Threading.Tasks;

namespace RevenantLauncher.ViewModels
{
    public class ConfirmDialogViewModel : ViewModelBase
    {
        private string _title = "";
        private string _message = "";
        private string _confirmText = "Да";
        private string _cancelText = "Отмена";
        private TaskCompletionSource<bool>? _tcs;

        public event Action? DialogClosed;

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string Message { get => _message; set => SetProperty(ref _message, value); }
        public string ConfirmText { get => _confirmText; set => SetProperty(ref _confirmText, value); }
        public string CancelText { get => _cancelText; set => SetProperty(ref _cancelText, value); }

        /// <summary>Показывает диалог и ждёт ответа пользователя</summary>
        public Task<bool> ShowAsync(string title, string message, string confirmText = "Да", string cancelText = "Отмена")
        {
            Title = title;
            Message = message;
            ConfirmText = confirmText;
            CancelText = cancelText;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _tcs.Task;
        }

        public void Confirm()
        {
            DialogClosed?.Invoke();
            _tcs?.TrySetResult(true);
        }

        public void Cancel()
        {
            DialogClosed?.Invoke();
            _tcs?.TrySetResult(false);
        }
    }
}
