using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace CBServersPlaynite
{
    public class CbGame : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string BackendId { get; set; }
        public string AssetId { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string[] Modes { get; set; }
        public string AssetRoot { get; set; }

        private bool installed;
        private string installPath;
        private int onlinePlayers;
        private int onlineServers;

        public bool Installed
        {
            get { return installed; }
            set
            {
                if (installed == value) return;
                installed = value;
                Notify(nameof(Installed));
                Notify(nameof(StatusBadge));
                Notify(nameof(PlayButtonText));
                Notify(nameof(SubText));
            }
        }

        public string InstallPath
        {
            get { return installPath; }
            set { installPath = value; Notify(nameof(InstallPath)); }
        }

        public int OnlinePlayers
        {
            get { return onlinePlayers; }
            set
            {
                if (onlinePlayers == value) return;
                onlinePlayers = value;
                Notify(nameof(OnlinePlayers));
                Notify(nameof(OnlineShort));
                Notify(nameof(OnlineVisibility));
                Notify(nameof(SubText));
            }
        }

        public int OnlineServers
        {
            get { return onlineServers; }
            set
            {
                if (onlineServers == value) return;
                onlineServers = value;
                Notify(nameof(OnlineServers));
                Notify(nameof(OnlineShort));
                Notify(nameof(SubText));
            }
        }

        public string CapsulePath { get { return FindAsset("capsule.jpg") ?? FindAsset("capsule.png"); } }
        public string HeroPath { get { return FindAsset("hero.jpg"); } }
        public string LogoPath { get { return FindAsset("logo.png"); } }

        public string StatusBadge { get { return Installed ? "INSTALADO" : "NO INSTALADO"; } }
        public string PlayButtonText { get { return Installed ? "JUGAR" : "INSTALAR"; } }
        public string OnlineShort { get { return OnlinePlayers + " EN LÍNEA"; } }
        public Visibility OnlineVisibility
        {
            get { return OnlinePlayers > 0 || OnlineServers > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string SubText
        {
            get
            {
                if (!Installed) return "Sin instalar";
                if (OnlinePlayers > 0) return OnlinePlayers + " jugadores · " + OnlineServers + " servidores";
                return "Instalado · sin jugadores ahora";
            }
        }

        public bool SupportsModes { get { return Modes != null && Modes.Length > 0; } }

        public string DefaultMode
        {
            get
            {
                if (Modes == null || Modes.Length == 0) return null;
                return Modes.Contains("mp") ? "mp" : Modes[0];
            }
        }

        public string ModeSummary
        {
            get
            {
                if (Modes == null || Modes.Length == 0) return "Un solo modo";
                return string.Join(" · ", Modes.Select(ModeLabels.Label));
            }
        }

        private string FindAsset(string file)
        {
            if (string.IsNullOrEmpty(AssetRoot)) return null;
            var path = Path.Combine(AssetRoot, file);
            return File.Exists(path) ? path : null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class GameCount
    {
        public int Players { get; set; }
        public int Servers { get; set; }
    }

    public class ServerInfo
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Map { get; set; }
        public string GameType { get; set; }
        public string Mode { get; set; }
        public string Region { get; set; }
        public string CountryName { get; set; }
        public string TagLabel { get; set; }
        public string TagNote { get; set; }
        public int Players { get; set; }
        public int MaxPlayers { get; set; }
        public int Bots { get; set; }
        public bool Featured { get; set; }

        public string ModeText { get { return ModeLabels.Label(Mode).ToUpperInvariant(); } }
        public string PlayersText { get { return MaxPlayers > 0 ? Players + " / " + MaxPlayers : Players.ToString(); } }
        public string RegionText { get { return string.IsNullOrEmpty(Region) ? "—" : Region; } }
        public string TagText { get { return string.IsNullOrEmpty(TagLabel) ? null : TagLabel.ToUpperInvariant(); } }
        public Visibility TagVisibility { get { return string.IsNullOrEmpty(TagLabel) ? Visibility.Collapsed : Visibility.Visible; } }
        public string SubText
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Map)) parts.Add(Map);
                if (!string.IsNullOrEmpty(GameType)) parts.Add(GameType.ToUpperInvariant());
                if (Bots > 0) parts.Add(Bots + " bots");
                return string.Join(" · ", parts);
            }
        }
    }

    public static class ModeLabels
    {
        public static string Label(string mode)
        {
            switch (mode)
            {
                case "sp": return "Campaña";
                case "mp": return "Multijugador";
                case "zm": return "Zombis";
                case "sv": return "Supervivencia";
                case "on": return "Online";
                case "off": return "Offline";
                default: return string.IsNullOrEmpty(mode) ? "Jugar" : mode.ToUpperInvariant();
            }
        }
    }

    public static class Catalog
    {
        public static List<CbGame> Create(string assetsRoot)
        {
            return new List<CbGame>
            {
                Game(assetsRoot, "cod1", "cod1", "cod1", "Call of Duty", "CoD 1", new[] { "sp", "mp" }),
                Game(assetsRoot, "coduo", "coduo", "coduo", "Call of Duty: United Offensive", "CoD UO", new[] { "sp", "mp" }),
                Game(assetsRoot, "cod2", "cod2x", "cod2x", "Call of Duty 2", "CoD 2", new[] { "sp", "mp" }),
                Game(assetsRoot, "cod4", "cod4x", "cod4x", "Call of Duty 4: Modern Warfare", "CoD 4", new[] { "sp", "mp" }),
                Game(assetsRoot, "waw", "t4", "t4", "Call of Duty: World at War", "WaW", new[] { "sp", "mp", "zm" }),
                Game(assetsRoot, "bo1", "t5", "t5", "Call of Duty: Black Ops", "BO1", new[] { "sp", "mp", "zm" }),
                Game(assetsRoot, "mw2", "iw4x", "iw4x", "Call of Duty: Modern Warfare 2", "MW2", new[] { "sp", "mp" }),
                Game(assetsRoot, "bo2", "t6", "t6", "Call of Duty: Black Ops II", "BO2", new[] { "sp", "mp", "zm" }),
                Game(assetsRoot, "mw3", "iw5", "iw5", "Call of Duty: Modern Warfare 3", "MW3", new[] { "sp", "mp" }),
                Game(assetsRoot, "bo3", "boiii", "bo3", "Call of Duty: Black Ops III", "BO3", new string[0]),
                Game(assetsRoot, "ghosts", "iw6x", "iw6x", "Call of Duty: Ghosts", "Ghosts", new[] { "sp", "mp" }),
                Game(assetsRoot, "aw", "s1x", "s1x", "Call of Duty: Advanced Warfare", "AW", new[] { "sp", "mp", "zm", "sv" }),
                Game(assetsRoot, "mwr", "h1-mod", "h1-mod", "Call of Duty: Modern Warfare Remastered", "MWR", new[] { "sp", "mp" }),
                Game(assetsRoot, "iw", "iw7-mod", "iw7-mod", "Call of Duty: Infinite Warfare", "IW", new string[0]),
                Game(assetsRoot, "ww2", "s2x", "s2x", "Call of Duty: WWII", "WWII", new[] { "sp", "mp", "zm" }),
                Game(assetsRoot, "mw2r", "h2-mod", "mw2r", "Call of Duty: MW2 Campaign Remastered", "MW2R", new string[0]),
                Game(assetsRoot, "bo4", "bo4", "bo4", "Call of Duty: Black Ops 4", "BO4", new[] { "on", "off" }),
                Game(assetsRoot, "hmw", "hmw-mod", "hmw", "HorizonMW", "HMW", new string[0])
            };
        }

        private static CbGame Game(string assetsRoot, string id, string assetId, string backendId, string name, string shortName, string[] modes)
        {
            return new CbGame
            {
                Id = id,
                AssetId = assetId,
                BackendId = backendId,
                Name = name,
                ShortName = shortName,
                Modes = modes,
                AssetRoot = Path.Combine(assetsRoot, assetId)
            };
        }
    }
}
