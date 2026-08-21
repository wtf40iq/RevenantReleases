using System.Collections.Generic;

namespace RevenantLauncher.Models
{
    public class LauncherData
    {
        public LauncherSettings Settings { get; set; } = new();
        public List<AccountModel> Accounts { get; set; } = new();
        public List<GameVersion> InstalledVersions { get; set; } = new();
        public List<PlaySession> PlaySessions { get; set; } = new();
    }
}