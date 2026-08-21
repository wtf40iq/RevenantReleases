namespace RevenantLauncher.Models
{
    public class LauncherSettings
    {
        // Java / RAM
        public int MinRamMb { get; set; } = 1024;
        public int MaxRamMb { get; set; } = 4096;
        public string? JavaPath { get; set; }
        public bool AutoDetectJava { get; set; } = true;
        public string JvmArgs { get; set; } = "-XX:+UnlockExperimentalVMOptions -XX:+UseG1GC";

        // Game paths
        public string GameDirectory { get; set; } = string.Empty;

        // Window
        public int GameWindowWidth { get; set; } = 1280;
        public int GameWindowHeight { get; set; } = 720;
        public bool Fullscreen { get; set; } = false;

        // Launcher behavior
        public bool CloseLauncherOnGameStart { get; set; } = false;
        public bool KeepLauncherOpen { get; set; } = true;
        public bool ShowGameLogs { get; set; } = true;

        // Sounds
        public bool SoundsEnabled { get; set; } = true;
        public double SoundsVolume { get; set; } = 0.5;

        // Last used
        public string LastSelectedVersion { get; set; } = "1.20.1";
        public string LastSelectedAccountId { get; set; } = string.Empty;

        /// <summary>Последний ник аккаунта Revenant — для автозаполнения в окне входа</summary>
        public string LastRevenantUsername { get; set; } = string.Empty;

        // UI
        public string Language { get; set; } = "ru";
        public string Theme { get; set; } = "Standart";
        public string Banner { get; set; } = "banner1.jpg";
        public string SidebarStyle { get; set; } = "Gradient";
    }
}