using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CBServersPlaynite
{
    public class LauncherService
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly string[] ServerHosts = { "https://servers.cbservers.xyz", "https://servers.cbservers.dev" };

        private readonly string settingsPath;
        private readonly HttpClient http;

        public string PluginRoot { get; private set; }
        public PluginSettings Settings { get; private set; }
        public string LauncherPath { get; private set; }
        public string DataRoot { get; private set; }

        public string PropertiesPath
        {
            get { return DataRoot == null ? null : Path.Combine(DataRoot, "user", "properties.json"); }
        }

        static LauncherService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        public LauncherService(string pluginRoot, string userDataPath)
        {
            PluginRoot = pluginRoot;
            settingsPath = Path.Combine(userDataPath, "settings.json");
            Settings = PluginSettings.Load(settingsPath);
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CBServersPlaynite/1.0");
            Reload();
        }

        public void Reload()
        {
            LauncherPath = DetectLauncher();
            DataRoot = DetectDataRoot();
        }

        public string GetProtocolExecutable()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\cbservers\shell\open\command"))
                {
                    var command = key == null ? null : key.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(command)) return null;

                    var match = Regex.Match(command, "^\"([^\"]+)\"");
                    if (!match.Success) match = Regex.Match(command, @"^(\S+)");
                    if (!match.Success) return null;

                    var path = match.Groups[1].Value;
                    return File.Exists(path) ? path : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private string DetectLauncher()
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(Settings.LauncherPath)) candidates.Add(Settings.LauncherPath);

            var protocol = GetProtocolExecutable();
            if (protocol != null) candidates.Add(protocol);

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(profile, "Downloads", "cb-launcher.exe"));
            candidates.Add(Path.Combine(profile, "Downloads", "CB Launcher", "cb-launcher.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "cb-launcher.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cbservers", "cb-launcher.exe"));
            candidates.Add(Path.Combine(PluginRoot, "cb-launcher.exe"));

            return candidates.FirstOrDefault(File.Exists);
        }

        private string DetectDataRoot()
        {
            if (!string.IsNullOrEmpty(LauncherPath))
            {
                var exeDir = Path.GetDirectoryName(LauncherPath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    var portable = Path.Combine(exeDir, "cbservers");
                    if (Directory.Exists(portable)) return portable;
                }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cbservers");
        }

        public void SetLauncherPath(string path)
        {
            Settings.LauncherPath = path;
            Settings.Save(settingsPath);
            Reload();
        }

        public void SaveSettings()
        {
            Settings.Save(settingsPath);
        }

        public bool IsRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("cb-launcher");
                foreach (var process in processes) process.Dispose();
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public Dictionary<string, string> ReadProperties()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = PropertiesPath;
            if (path == null || !File.Exists(path)) return result;

            try
            {
                var parsed = Json.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (parsed != null)
                {
                    foreach (var pair in parsed) result[pair.Key] = pair.Value;
                }
            }
            catch
            {
            }

            return result;
        }

        public void SetProperty(string key, string value)
        {
            var path = PropertiesPath;
            if (path == null) throw new InvalidOperationException("No se ha localizado la carpeta de datos de CB Launcher.");

            var properties = ReadProperties();
            properties[key] = value;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, Json.Serialize(properties));
        }

        public void ApplyState(CbGame game, Dictionary<string, string> properties)
        {
            string installed;
            properties.TryGetValue(game.BackendId + "-is-installed", out installed);
            game.Installed = string.Equals(installed, "true", StringComparison.OrdinalIgnoreCase);

            string installPath;
            properties.TryGetValue(game.BackendId + "-install", out installPath);
            game.InstallPath = installPath;
        }

        public string GetPlayerName(Dictionary<string, string> properties)
        {
            string name;
            if (properties.TryGetValue("launcher-global-player-name", out name) && !string.IsNullOrWhiteSpace(name)) return name;

            foreach (var pair in properties)
            {
                if (pair.Key.EndsWith("-player-name-override", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        public bool GetBoolProperty(Dictionary<string, string> properties, string key)
        {
            string value;
            return properties.TryGetValue(key, out value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public void Launch(CbGame game, string mode)
        {
            RequireLauncher();

            if (IsRunning() && GetProtocolExecutable() != null)
            {
                OpenUri("cbservers://play/" + game.Id + (string.IsNullOrEmpty(mode) ? "" : "/" + mode));
                return;
            }

            StartExe("-launch " + game.Id + (string.IsNullOrEmpty(mode) ? "" : " -mode " + mode));
        }

        public void Install(CbGame game)
        {
            RequireLauncher();

            if (IsRunning() && GetProtocolExecutable() != null)
            {
                OpenUri("cbservers://install/" + game.Id);
                return;
            }

            StartExe("-install " + game.Id);
        }

        public void OpenGamePage(CbGame game)
        {
            if (GetProtocolExecutable() != null)
            {
                OpenUri("cbservers://game/" + game.Id);
                return;
            }

            OpenLauncher();
        }

        public void OpenMods(CbGame game)
        {
            if (GetProtocolExecutable() != null)
            {
                OpenUri("cbservers://mods/" + game.Id);
                return;
            }

            OpenLauncher();
        }

        public void OpenLauncher()
        {
            RequireLauncher();
            StartExe("");
        }

        public void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (Directory.Exists(path) || File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
        }

        public void OpenDataFolder()
        {
            OpenFolder(DataRoot);
        }

        private void RequireLauncher()
        {
            if (string.IsNullOrEmpty(LauncherPath) || !File.Exists(LauncherPath))
            {
                throw new InvalidOperationException("No se ha encontrado cb-launcher.exe. Indica la ruta en Ajustes.");
            }
        }

        private void StartExe(string arguments)
        {
            RequireLauncher();
            var info = new ProcessStartInfo
            {
                FileName = LauncherPath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(LauncherPath),
                UseShellExecute = true
            };
            Process.Start(info);
        }

        private static void OpenUri(string uri)
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }

        public async Task<Dictionary<string, GameCount>> GetPlayerCountsAsync()
        {
            var body = await GetJsonAsync("/v1/player-counts").ConfigureAwait(false);
            var result = new Dictionary<string, GameCount>(StringComparer.OrdinalIgnoreCase);

            var root = Json.Deserialize<Dictionary<string, object>>(body);
            object gamesObject;
            if (root == null || !root.TryGetValue("games", out gamesObject)) return result;

            var games = gamesObject as Dictionary<string, object>;
            if (games == null) return result;

            foreach (var pair in games)
            {
                var data = pair.Value as Dictionary<string, object>;
                if (data == null) continue;

                result[pair.Key] = new GameCount
                {
                    Players = ToInt(GetValue(data, "players")),
                    Servers = ToInt(GetValue(data, "servers"))
                };
            }

            return result;
        }

        public async Task<List<ServerInfo>> GetServersAsync(string backendId)
        {
            var result = new List<ServerInfo>();
            var body = await GetJsonAsync("/v1/servers?game=" + Uri.EscapeDataString(backendId), true).ConfigureAwait(false);

            var root = Json.Deserialize<Dictionary<string, object>>(body);
            object serversObject;
            if (root == null || !root.TryGetValue("servers", out serversObject)) return result;

            var items = serversObject as System.Collections.IList;
            if (items == null) return result;

            foreach (var item in items)
            {
                var data = item as Dictionary<string, object>;
                if (data == null) continue;

                var server = new ServerInfo
                {
                    Name = GetString(data, "name"),
                    Address = GetString(data, "id"),
                    Map = GetString(data, "map"),
                    GameType = GetString(data, "gametype"),
                    Mode = GetString(data, "mode"),
                    Region = GetString(data, "region"),
                    CountryName = GetString(data, "countryName"),
                    Players = ToInt(GetValue(data, "players")),
                    MaxPlayers = ToInt(GetValue(data, "maxPlayers")),
                    Bots = ToInt(GetValue(data, "bots"))
                };

                var tag = GetValue(data, "tag") as Dictionary<string, object>;
                if (tag != null)
                {
                    server.TagLabel = GetString(tag, "label");
                    server.TagNote = GetString(tag, "note");
                    server.Featured = GetValue(tag, "featured") != null;
                }

                if (string.IsNullOrEmpty(server.Name)) server.Name = server.Address;
                result.Add(server);
            }

            return result
                .OrderByDescending(server => server.Featured)
                .ThenByDescending(server => server.Players)
                .ToList();
        }

        private async Task<string> GetJsonAsync(string path, bool emptyOnBadRequest = false)
        {
            Exception last = null;

            foreach (var host in ServerHosts)
            {
                try
                {
                    using (var response = await http.GetAsync(host + path).ConfigureAwait(false))
                    {
                        if (emptyOnBadRequest && response.StatusCode == HttpStatusCode.BadRequest) return "{}";
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            throw new InvalidOperationException("No se pudo conectar con los servidores de CB.", last);
        }

        private static object GetValue(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value : null;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            var value = GetValue(data, key);
            return value == null ? null : value.ToString();
        }

        private static int ToInt(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }
    }
}
