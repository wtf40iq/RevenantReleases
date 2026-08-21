namespace RevenantLauncherSetup.Models
{
    public class InstallOptions
    {
        public string InstallPath { get; set; } = string.Empty;
        public bool IsUpdate { get; set; }
        public int LauncherProcessId { get; set; }
        public bool CreateDesktopShortcut { get; set; } = true;
        public bool CreateStartMenuShortcut { get; set; } = true;
        public bool LaunchAfterInstall { get; set; } = true;
        public bool LicenseAccepted { get; set; } = false;

        public static string GetDefaultInstallPath()
        {
            var localAppData = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(localAppData, "Programs", "RevenantLauncher");
        }
    }
}