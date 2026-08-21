using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using RevenantLauncherSetup.Models;

namespace RevenantLauncherSetup.ViewModels
{
    public enum SetupPage
    {
        Welcome,
        License,
        Location,
        Installing,
        Finish,
        UninstallConfirm,
        Uninstalling,
        UninstallFinish
    }

    public class SetupViewModel : ViewModelBase 
    {
        private SetupPage _currentPage;
        private bool _isUninstallMode;
        private bool _isUpdateMode;
        private int _launcherProcessId;
        private string _installPath = InstallOptions.GetDefaultInstallPath();
        private bool _createDesktopShortcut = true;
        private bool _createStartMenuShortcut = true;
        private bool _launchAfterInstall = true;
        private bool _licenseAccepted = false;
        private bool _deleteUserData = false;

        private double _installProgress;
        private double _targetProgress;
        private DispatcherTimer? _progressTicker;
        private string _installStatus = "Подготовка...";
        private bool _isInstalling;
        private bool _installFailed;
        private string _errorMessage = "";

        public SetupViewModel(bool uninstallMode = false, bool updateMode = false, string? updatePath = null, int launcherProcessId = 0)
        {
            _isUninstallMode = uninstallMode;
            _isUpdateMode = updateMode;
            _launcherProcessId = launcherProcessId;
            if (updateMode && !string.IsNullOrWhiteSpace(updatePath))
                _installPath = updatePath;

            _currentPage = uninstallMode
                ? SetupPage.UninstallConfirm
                : updateMode ? SetupPage.Installing : SetupPage.Welcome;
        }

        public SetupPage CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(IsWelcomePage));
                    OnPropertyChanged(nameof(IsLicensePage));
                    OnPropertyChanged(nameof(IsLocationPage));
                    OnPropertyChanged(nameof(IsInstallingPage));
                    OnPropertyChanged(nameof(IsFinishPage));
                    OnPropertyChanged(nameof(IsUninstallConfirmPage));
                    OnPropertyChanged(nameof(IsUninstallingPage));
                    OnPropertyChanged(nameof(IsUninstallFinishPage));
                    OnPropertyChanged(nameof(IsProcessingPage));
                    OnPropertyChanged(nameof(CanClose));
                    OnPropertyChanged(nameof(InstallingTitle));
                    OnPropertyChanged(nameof(InstallingSubtitle));
                    OnPropertyChanged(nameof(InstallingHint));
                }
            }
        }

        public bool IsUninstallMode => _isUninstallMode;
        public bool IsUpdateMode => _isUpdateMode;

        public bool IsWelcomePage => CurrentPage == SetupPage.Welcome;
        public bool IsLicensePage => CurrentPage == SetupPage.License;
        public bool IsLocationPage => CurrentPage == SetupPage.Location;
        public bool IsInstallingPage => CurrentPage == SetupPage.Installing;
        public bool IsFinishPage => CurrentPage == SetupPage.Finish;
        public bool IsUninstallConfirmPage => CurrentPage == SetupPage.UninstallConfirm;
        public bool IsUninstallingPage => CurrentPage == SetupPage.Uninstalling;
        public bool IsUninstallFinishPage => CurrentPage == SetupPage.UninstallFinish;

        /// <summary>True когда идёт установка ИЛИ удаление</summary>
        public bool IsProcessingPage => IsInstallingPage || IsUninstallingPage;

        public bool CanClose => !IsInstalling && !IsProcessingPage;

        // ===== ТЕКСТЫ (меняются в зависимости от режима) =====

        public string InstallingTitle => IsUninstallingPage
            ? "Удаляем..."
            : IsUpdateMode ? "Обновляем..." : "Устанавливаем...";

        public string InstallingSubtitle => IsUninstallingPage
            ? "Пожалуйста, подождите. Удаление займёт несколько секунд."
            : IsUpdateMode
                ? "Новая версия устанавливается автоматически."
                : "Пожалуйста, подождите. Установка займёт несколько секунд.";

        public string InstallingHint => IsUninstallingPage
            ? "Не закрывайте окно во время удаления"
            : IsUpdateMode ? "После завершения лаунчер запустится снова" : "Не закрывайте окно во время установки";

        // ===== Опции установки =====
        public string InstallPath
        {
            get => _installPath;
            set => SetProperty(ref _installPath, value);
        }

        public bool CreateDesktopShortcut
        {
            get => _createDesktopShortcut;
            set => SetProperty(ref _createDesktopShortcut, value);
        }

        public bool CreateStartMenuShortcut
        {
            get => _createStartMenuShortcut;
            set => SetProperty(ref _createStartMenuShortcut, value);
        }

        public bool LaunchAfterInstall
        {
            get => _launchAfterInstall;
            set => SetProperty(ref _launchAfterInstall, value);
        }

        public bool LicenseAccepted
        {
            get => _licenseAccepted;
            set
            {
                if (SetProperty(ref _licenseAccepted, value))
                    OnPropertyChanged(nameof(CanProceedFromLicense));
            }
        }

        public bool CanProceedFromLicense => _licenseAccepted;

        public bool DeleteUserData
        {
            get => _deleteUserData;
            set => SetProperty(ref _deleteUserData, value);
        }

        // ===== Прогресс =====
        public double InstallProgress
        {
            get => _installProgress;
            set
            {
                if (SetProperty(ref _installProgress, value))
                    OnPropertyChanged(nameof(InstallProgressText));
            }
        }

        public string InstallProgressText => $"{(int)InstallProgress}%";

        /// <summary>Ширина заливки прогресс-бара в пикселях (дорожка 380px) —
        /// прямая привязка без конвертера, чтобы полоска точно обновлялась</summary>
        public double InstallProgressWidth => _installProgress * 3.8;

        public string InstallStatus
        {
            get => _installStatus;
            set => SetProperty(ref _installStatus, value);
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                if (SetProperty(ref _isInstalling, value))
                    OnPropertyChanged(nameof(CanClose));
            }
        }

        public bool InstallFailed
        {
            get => _installFailed;
            set => SetProperty(ref _installFailed, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        // ===== Навигация =====
        public void GoNext()
        {
            switch (CurrentPage)
            {
                case SetupPage.Welcome:
                    CurrentPage = SetupPage.License;
                    break;
                case SetupPage.License:
                    if (LicenseAccepted) CurrentPage = SetupPage.Location;
                    break;
                case SetupPage.Location:
                    CurrentPage = SetupPage.Installing;
                    break;
            }
        }

        public void GoBack()
        {
            switch (CurrentPage)
            {
                case SetupPage.License:
                    CurrentPage = SetupPage.Welcome;
                    break;
                case SetupPage.Location:
                    CurrentPage = SetupPage.License;
                    break;
            }
        }

        public InstallOptions BuildOptions()
        {
            return new InstallOptions
            {
                InstallPath = InstallPath,
                IsUpdate = IsUpdateMode,
                LauncherProcessId = _launcherProcessId,
                CreateDesktopShortcut = CreateDesktopShortcut,
                CreateStartMenuShortcut = CreateStartMenuShortcut,
                LaunchAfterInstall = LaunchAfterInstall,
                LicenseAccepted = LicenseAccepted
            };
        }

        public void UpdateProgress(double progress, string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Только поднимаем цель — тикер плавно доведёт полоску без скачков
                _targetProgress = Math.Max(_targetProgress, Math.Max(0, Math.Min(100, progress)));
                InstallStatus = status;
                StartProgressTicker();
            });
        }

        /// <summary>
        /// Плавное "ползение" полоски к цели: быстро сразу после скачка,
        /// затем замедление — прогресс никогда не прыгает мгновенно.
        /// </summary>
        private void StartProgressTicker()
        {
            if (_progressTicker != null) return;

            _progressTicker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _progressTicker.Tick += (_, _) =>
            {
                var diff = _targetProgress - _installProgress;

                if (diff <= 0.05)
                {
                    if (_targetProgress >= 100)
                    {
                        SetInstallProgress(100);
                        _progressTicker.Stop();
                        _progressTicker = null;
                    }
                    return;
                }

                var step = Math.Max(0.15, diff * 0.10);
                SetInstallProgress(_installProgress + Math.Min(step, diff));
            };
            _progressTicker.Start();
        }

        private void SetInstallProgress(double value)
        {
            if (SetProperty(ref _installProgress, value))
            {
                OnPropertyChanged(nameof(InstallProgressText));
                OnPropertyChanged(nameof(InstallProgressWidth));
            }
        }

        // ===== Тексты интерфейса =====
        private static string ProductVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.2.1";

        public string VersionText => $"Revenant Launcher v{ProductVersion}";
        public string EditionText => IsUninstallMode
            ? $"Деинсталлятор v{ProductVersion}"
            : IsUpdateMode ? "Автообновление" : $"Установщик v{ProductVersion}";

        /// <summary>Заголовок окна — зависит от режима (чтобы при удалении не было надписей установки)</summary>
        public string WindowTitle => IsUninstallMode
            ? "Удаление Revenant Launcher"
            : IsUpdateMode ? "Обновление Revenant Launcher" : "Установка Revenant Launcher";

        // ===== Текст лицензии =====
        public string LicenseText => @"Лицензионное соглашение конечного пользователя
Revenant Launcher v1.1.0

Пожалуйста, внимательно прочитайте это соглашение перед использованием программы. Устанавливая Revenant Launcher, вы соглашаетесь со всеми условиями, изложенными ниже.


1. ОБЩИЕ ПОЛОЖЕНИЯ

Revenant Launcher (далее — «Программа») является бесплатным программным обеспечением, предназначенным для запуска игры Minecraft и управления её версиями, модификациями и ресурспаками.

Правообладателем Программы является lotesnowhallen (далее — «Автор»).


2. УСЛОВИЯ ИСПОЛЬЗОВАНИЯ

2.1. Программа предоставляется «как есть» (as is), без каких-либо гарантий явных или подразумеваемых.

2.2. Вы можете свободно использовать Программу в личных, некоммерческих целях.

2.3. Запрещается:
- Продавать Программу или её модифицированные версии
- Распространять Программу от своего имени, выдавая её за собственную разработку
- Использовать Программу для нарушения условий использования игры Minecraft, установленных компанией Mojang Studios и Microsoft
- Использовать Программу для распространения вредоносного ПО


3. ОТНОШЕНИЕ К MINECRAFT

3.1. Revenant Launcher не является официальным продуктом Mojang Studios или Microsoft.

3.2. Minecraft является товарным знаком Mojang Studios. Все права на игру принадлежат её правообладателям.

3.3. Программа предоставляет возможность запуска игры в offline-режиме, что не заменяет и не отменяет необходимости приобретения лицензионной копии игры Minecraft для игры на официальных серверах и получения полного игрового опыта.

3.4. Автор Программы настоятельно рекомендует приобретать лицензионную копию Minecraft и не несёт ответственности за использование Программы в целях, нарушающих правила Mojang Studios.


4. МОДИФИКАЦИИ И СТОРОННИЙ КОНТЕНТ

4.1. Программа предоставляет возможность загрузки и установки модификаций (модов) и ресурспаков со стороннего сервиса Modrinth.

4.2. Автор Программы не несёт ответственности за:
- Работоспособность сторонних модов и ресурспаков
- Возможный вред от установленных модификаций (потеря игровых сохранений, проблемы с совместимостью)
- Действия и контент, размещённый на сторонних сервисах

4.3. Вся ответственность за использование сторонних модификаций лежит на пользователе.


5. ОГРАНИЧЕНИЕ ОТВЕТСТВЕННОСТИ

5.1. Автор не несёт ответственности за:
- Любые прямые или косвенные убытки, возникшие в результате использования Программы
- Потерю данных, сбои в работе операционной системы или других программ
- Проблемы с совместимостью с другим программным обеспечением
- Ущерб, причинённый использованием сторонних модификаций и ресурспаков

5.2. Использование Программы осуществляется на ваш собственный риск.


6. ДАННЫЕ ПОЛЬЗОВАТЕЛЯ

6.1. Программа хранит на вашем компьютере локально:
- Настройки лаунчера (папка %AppData%\RevenantLauncher\)
- Информацию об аккаунтах (никнеймы, UUID для offline-режима)
- Историю запусков игры
- Скачанные версии Minecraft, моды и ресурспаки

6.2. Программа не собирает и не передаёт ваши персональные данные третьим лицам.

6.3. Программа обращается к следующим внешним сервисам исключительно для своей работы:
- launchermeta.mojang.com — получение списка версий Minecraft
- meta.fabricmc.net — получение версий загрузчика Fabric
- maven.minecraftforge.net — получение версий загрузчика Forge
- api.modrinth.com — поиск и загрузка модификаций


7. ОБНОВЛЕНИЯ

7.1. Автор оставляет за собой право обновлять Программу и изменять её функциональность без предварительного уведомления.

7.2. Для получения обновлений может потребоваться повторная установка последней версии.


8. ПРЕКРАЩЕНИЕ ДЕЙСТВИЯ

8.1. Вы можете прекратить использование Программы в любой момент, удалив её через стандартное средство удаления программ Windows.

8.2. При удалении Программы вам будет предложено сохранить или удалить ваши пользовательские данные.


9. ИЗМЕНЕНИЯ В СОГЛАШЕНИИ

9.1. Автор оставляет за собой право изменять условия данного соглашения в будущих версиях Программы.

9.2. Продолжение использования Программы после выпуска новой версии означает ваше согласие с обновлёнными условиями.


10. КОНТАКТЫ

По всем вопросам, связанным с работой Программы, обращайтесь к автору:
- Telegram: @lotesnowhallen


Устанавливая Revenant Launcher, вы подтверждаете, что прочитали, поняли и согласны со всеми условиями данного лицензионного соглашения.

© 2026 lotesnowhallen. Все права защищены.";
    }
}