using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RevenantLauncher.ViewModels;
using System;

namespace RevenantLauncher
{
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? data)
        {
            if (data is null) return null;

            var vmName = data.GetType().FullName!;
            // "RevenantLauncher.ViewModels.SettingsViewModel" → "RevenantLauncher.Views.SettingsView"
            // "RevenantLauncher.ViewModels.VersionDialogViewModel" → "RevenantLauncher.Views.VersionDialog"
            // "RevenantLauncher.ViewModels.AddAccountDialogViewModel" → "RevenantLauncher.Views.AddAccountDialog"

            // Заменяем namespace
            var viewName = vmName.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);

            // Убираем суффикс "ViewModel" на конце
            if (viewName.EndsWith("ViewModel", StringComparison.Ordinal))
                viewName = viewName.Substring(0, viewName.Length - "ViewModel".Length);

            // Сначала пробуем найти "SettingsView"
            var type = Type.GetType(viewName + "View");
            if (type != null)
            {
                var control = (Control)Activator.CreateInstance(type)!;
                control.DataContext = data;
                return control;
            }

            // Если не нашли — пробуем без "View" (для "VersionDialog", "AddAccountDialog")
            type = Type.GetType(viewName);
            if (type != null)
            {
                var control = (Control)Activator.CreateInstance(type)!;
                control.DataContext = data;
                return control;
            }

            return new TextBlock { Text = "Not Found: " + viewName };
        }

        public bool Match(object? data) => data is ViewModelBase;
    }
}