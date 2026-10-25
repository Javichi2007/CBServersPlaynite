using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;

namespace CBServersPlaynite
{
    public class CbFriend
    {
        private static readonly string[] Palette =
        {
            "#FF6A1A", "#3B82F6", "#8B5CF6", "#10B981", "#F59E0B", "#EF4444", "#06B6D4", "#EC4899"
        };

        public string CbId { get; set; }
        public string Handle { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public string Game { get; set; }
        public string Mode { get; set; }
        public string Map { get; set; }
        public string Gametype { get; set; }
        public string ServerName { get; set; }
        public string Status { get; set; }
        public string Relation { get; set; }
        public bool Online { get; set; }
        public int Players { get; set; }
        public int MaxPlayers { get; set; }
        public long LastSeen { get; set; }

        public string Name
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                if (!string.IsNullOrWhiteSpace(Handle)) return Handle;
                return "Jugador de CB";
            }
        }

        public string NameText { get { return Name; } }

        public string HandleText
        {
            get { return string.IsNullOrWhiteSpace(Handle) ? "" : "@" + Handle; }
        }

        public string Initials
        {
            get
            {
                var parts = Name.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
                return "CB";
            }
        }

        public Brush AvatarBrush
        {
            get
            {
                var seed = string.IsNullOrEmpty(CbId) ? Name : CbId;
                var index = Math.Abs(seed.GetHashCode()) % Palette.Length;
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[index]));
                brush.Freeze();
                return brush;
            }
        }

        internal static long NormalizeTimestamp(long value)
        {
            if (value <= 0) return 0;
            return value > 100000000000L ? value / 1000 : value;
        }

        public string PresenceText
        {
            get
            {
                if (!Online)
                {
                    if (LastSeen <= 0) return "Desconectado";
                    var delta = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - NormalizeTimestamp(LastSeen);
                    if (delta < 60) return "Desconectado · hace un momento";
                    if (delta < 3600) return "Desconectado · hace " + (delta / 60) + " min";
                    if (delta < 86400) return "Desconectado · hace " + (delta / 3600) + " h";
                    return "Desconectado · hace " + (delta / 86400) + " d";
                }

                var parts = new List<string>();
                var game = Catalog.ShortName(Game);
                if (!string.IsNullOrWhiteSpace(game)) parts.Add(game);
                if (!string.IsNullOrWhiteSpace(Mode)) parts.Add(ModeLabels.Label(Mode));
                if (!string.IsNullOrWhiteSpace(Map)) parts.Add(Map);
                if (Players > 0) parts.Add(Players + (MaxPlayers > 0 ? "/" + MaxPlayers : "") + " jug.");

                if (parts.Count == 0 && !string.IsNullOrWhiteSpace(Status)) return Status;
                if (parts.Count == 0) return "En línea";
                return string.Join(" · ", parts);
            }
        }

        public Brush PresenceBrush
        {
            get
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Online ? "#35D399" : "#66737F"));
                brush.Freeze();
                return brush;
            }
        }

        public Visibility IncomingVisibility
        {
            get { return Relation == "incoming" ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility OutgoingVisibility
        {
            get { return Relation == "requested" || Relation == "outgoing" ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility FriendVisibility
        {
            get { return Relation == "friend" || string.IsNullOrEmpty(Relation) ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility OnlineVisibility
        {
            get { return Online ? Visibility.Visible : Visibility.Collapsed; }
        }
    }

    public class SocialSnapshot
    {
        public List<CbFriend> Friends { get; set; }
        public List<CbFriend> Incoming { get; set; }
        public List<CbFriend> Outgoing { get; set; }
        public string ProfileName { get; set; }
        public string ProfileHandle { get; set; }

        public SocialSnapshot()
        {
            Friends = new List<CbFriend>();
            Incoming = new List<CbFriend>();
            Outgoing = new List<CbFriend>();
        }

        public int OnlineCount { get { return Friends.Count(friend => friend.Online); } }
        public int RequestCount { get { return Incoming.Count + Outgoing.Count; } }
    }

    public class SocialService
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly string[] Hosts = { "https://social.cbservers.xyz", "https://social.cbservers.dev" };

        private readonly LauncherService launcher;
        private readonly HttpClient http;
        private ECDsaCng signer;
        private string publicKeyBase64;

        public bool IsLinked { get; private set; }
        public string LinkError { get; private set; }
        public string ProfileName { get; private set; }
        public string ProfileHandle { get; private set; }

        static SocialService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        public SocialService(LauncherService launcher)
        {
            this.launcher = launcher;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CBServersPlaynite/1.1");
            ReloadIdentity();
        }

        public void ReloadIdentity()
        {
            DisposeSigner();
            IsLinked = false;
            LinkError = null;
            ProfileName = null;
            ProfileHandle = null;

            var properties = launcher.ReadProperties();
            if (properties.Count == 0)
            {
                LinkError = "CB Launcher todavía no tiene datos en este PC.";
                return;
            }

            ReadProfile(properties);

            string protectedKey;
            if (!properties.TryGetValue("launcher-cb-device-key", out protectedKey) || string.IsNullOrWhiteSpace(protectedKey))
            {
                LinkError = "Abre CB Launcher una vez para crear tu perfil de CB.";
                return;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(protectedKey);
                var der = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                LoadKey(der);
                IsLinked = true;
            }
            catch (CryptographicException)
            {
                LinkError = "Windows no pudo abrir la identidad de CB. Abre CB Launcher una vez y reintenta.";
            }
            catch (Exception)
            {
                LinkError = "La identidad de CB guardada no es válida. Abre CB Launcher una vez y reintenta.";
            }
        }

        private void ReadProfile(Dictionary<string, string> properties)
        {
            string profileJson;
            if (!properties.TryGetValue("launcher-cb-profile", out profileJson) || string.IsNullOrWhiteSpace(profileJson)) return;

            try
            {
                var profile = Json.Deserialize<Dictionary<string, object>>(profileJson);
                if (profile == null) return;
                ProfileName = GetString(profile, "displayName");
                ProfileHandle = GetString(profile, "handle");
            }
            catch
            {
            }
        }

        private void LoadKey(byte[] der)
        {
            int position = 0;
            var sequence = ReadElement(der, ref position, 0x30);
            position = 0;
            ReadElement(sequence, ref position, 0x03);
            ReadElement(sequence, ref position, 0x02);
            var x = Pad32(ReadElement(sequence, ref position, 0x02));
            var y = Pad32(ReadElement(sequence, ref position, 0x02));
            var d = Pad32(ReadElement(sequence, ref position, 0x02));

            var blob = new byte[8 + 96];
            WriteInt32(blob, 0, 0x32534345); // BCRYPT_ECDSA_PRIVATE_P256_MAGIC
            WriteInt32(blob, 4, 32);
            Array.Copy(x, 0, blob, 8, 32);
            Array.Copy(y, 0, blob, 40, 32);
            Array.Copy(d, 0, blob, 72, 32);

            var key = CngKey.Import(blob, CngKeyBlobFormat.EccPrivateBlob);
            signer = new ECDsaCng(key);

            var publicKey = new byte[65];
            publicKey[0] = 0x04;
            Array.Copy(x, 0, publicKey, 1, 32);
            Array.Copy(y, 0, publicKey, 33, 32);
            publicKeyBase64 = Convert.ToBase64String(publicKey);
        }

        public async Task<SocialSnapshot> GetFriendsAsync()
        {
            ReloadIdentity();
            RequireLinked();

            var document = await PostJsonAsync("/v1/friends/list", TsBody()).ConfigureAwait(false);
            var snapshot = new SocialSnapshot
            {
                ProfileName = ProfileName,
                ProfileHandle = ProfileHandle
            };

            object friends;
            if (document.TryGetValue("friends", out friends)) snapshot.Friends = ParsePeople(friends, "friend");
            if (document.TryGetValue("incoming", out friends)) snapshot.Incoming = ParsePeople(friends, "incoming");
            if (document.TryGetValue("outgoing", out friends)) snapshot.Outgoing = ParsePeople(friends, "requested");

            return snapshot;
        }

        public Task AddFriendAsync(string handle)
        {
            ReloadIdentity();
            RequireLinked();
            var clean = (handle ?? "").Trim().TrimStart('@');
            if (clean.Length == 0) throw new InvalidOperationException("Escribe el handle de CB de la persona.");
            if (clean.Length > 32) throw new InvalidOperationException("Ese handle es demasiado largo.");

            var body = "{\"ts\":" + Now() + ",\"handle\":" + Json.Serialize(clean) + "}";
            return PostActionAsync("/v1/friends/add", body);
        }

        public Task AcceptFriendAsync(string cbId) { return FriendActionAsync("/v1/friends/accept", cbId); }
        public Task DeclineFriendAsync(string cbId) { return FriendActionAsync("/v1/friends/decline", cbId); }
        public Task CancelFriendAsync(string cbId) { return FriendActionAsync("/v1/friends/cancel", cbId); }
        public Task RemoveFriendAsync(string cbId) { return FriendActionAsync("/v1/friends/remove", cbId); }

        private Task FriendActionAsync(string path, string cbId)
        {
            ReloadIdentity();
            RequireLinked();
            if (string.IsNullOrWhiteSpace(cbId)) throw new InvalidOperationException("Ese jugador no tiene un identificador válido.");

            var body = "{\"ts\":" + Now() + ",\"cbId\":" + Json.Serialize(cbId) + "}";
            return PostActionAsync(path, body);
        }

        private void RequireLinked()
        {
            if (!IsLinked || signer == null || string.IsNullOrEmpty(publicKeyBase64))
            {
                throw new InvalidOperationException(LinkError ?? "Conecta tu cuenta de CB para usar los amigos.");
            }
        }

        private async Task<Dictionary<string, object>> PostJsonAsync(string path, string body)
        {
            var signature = Sign(body);
            Exception lastTransport = null;
            string lastServer = null;

            foreach (var host in Hosts)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, host + path))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        request.Headers.TryAddWithoutValidation("X-CB-Key", publicKeyBase64);
                        request.Headers.TryAddWithoutValidation("X-CB-Sig", signature);

                        using (var response = await http.SendAsync(request).ConfigureAwait(false))
                        {
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if ((int)response.StatusCode >= 500)
                            {
                                lastServer = "CB está teniendo problemas (" + (int)response.StatusCode + ").";
                                continue;
                            }

                            if (response.StatusCode != HttpStatusCode.OK)
                            {
                                throw new InvalidOperationException(ErrorMessage(response.StatusCode, text));
                            }

                            var document = Json.Deserialize<Dictionary<string, object>>(text);
                            if (document == null) throw new InvalidOperationException("CB devolvió una respuesta inesperada.");
                            return document;
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastTransport = ex;
                }
                catch (TaskCanceledException ex)
                {
                    lastTransport = ex;
                }
            }

            if (lastServer != null) throw new InvalidOperationException(lastServer);
            throw new InvalidOperationException("No se pudo conectar con CB. Comprueba tu conexión.", lastTransport);
        }

        private async Task PostActionAsync(string path, string body)
        {
            var signature = Sign(body);
            Exception lastTransport = null;
            string lastServer = null;

            foreach (var host in Hosts)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Post, host + path))
                    {
                        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        request.Headers.TryAddWithoutValidation("X-CB-Key", publicKeyBase64);
                        request.Headers.TryAddWithoutValidation("X-CB-Sig", signature);

                        using (var response = await http.SendAsync(request).ConfigureAwait(false))
                        {
                            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if ((int)response.StatusCode >= 500)
                            {
                                lastServer = "CB está teniendo problemas (" + (int)response.StatusCode + ").";
                                continue;
                            }

                            if (response.StatusCode != HttpStatusCode.OK)
                            {
                                throw new InvalidOperationException(ErrorMessage(response.StatusCode, text));
                            }

                            return;
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastTransport = ex;
                }
                catch (TaskCanceledException ex)
                {
                    lastTransport = ex;
                }
            }

            if (lastServer != null) throw new InvalidOperationException(lastServer);
            throw new InvalidOperationException("No se pudo conectar con CB. Comprueba tu conexión.", lastTransport);
        }

        private string Sign(string body)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
                var signature = signer.SignHash(digest);
                return Convert.ToBase64String(ToDer(signature));
            }
        }

        private static string ErrorMessage(HttpStatusCode status, string text)
        {
            var message = ParseError(text);
            if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
            {
                return "CB rechazó la identidad de este PC. Abre CB Launcher una vez y vuelve a intentarlo.";
            }
            if (!string.IsNullOrWhiteSpace(message)) return message;
            return "CB no pudo completar la operación (" + (int)status + ").";
        }

        private static string ParseError(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                var document = Json.Deserialize<Dictionary<string, object>>(text);
                if (document == null) return null;
                var error = GetString(document, "error");
                if (!string.IsNullOrWhiteSpace(error)) return error;
                var code = GetString(document, "code");
                if (!string.IsNullOrWhiteSpace(code)) return code;
            }
            catch
            {
            }
            return null;
        }

        private static List<CbFriend> ParsePeople(object value, string relation)
        {
            var result = new List<CbFriend>();
            var items = value as ArrayList;
            if (items == null) return result;

            foreach (var item in items)
            {
                var data = item as Dictionary<string, object>;
                if (data == null) continue;

                var friend = new CbFriend
                {
                    CbId = GetString(data, "cbId"),
                    Handle = GetString(data, "handle"),
                    DisplayName = GetString(data, "displayName"),
                    AvatarUrl = GetString(data, "avatarUrl"),
                    Game = GetString(data, "game"),
                    Mode = GetString(data, "mode"),
                    Map = GetString(data, "map"),
                    Gametype = GetString(data, "gametype"),
                    ServerName = GetString(data, "server"),
                    Status = GetString(data, "status"),
                    Relation = relation,
                    Online = GetBool(data, "online"),
                    Players = GetInt(data, "players"),
                    MaxPlayers = GetInt(data, "maxPlayers"),
                    LastSeen = CbFriend.NormalizeTimestamp(GetLong(data, "lastSeen"))
                };

                var reported = GetString(data, "relation");
                if (!string.IsNullOrWhiteSpace(reported)) friend.Relation = reported;
                result.Add(friend);
            }

            return result;
        }

        private static string TsBody()
        {
            return "{\"ts\":" + Now() + "}";
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value != null ? value.ToString() : null;
        }

        private static bool GetBool(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static int GetInt(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static long GetLong(Dictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToInt64(value); }
            catch { return 0; }
        }

        private static byte[] ReadElement(byte[] data, ref int position, byte tag)
        {
            if (data[position++] != tag) throw new InvalidDataException("Formato de clave inesperado.");
            var length = ReadLength(data, ref position);
            var value = new byte[length];
            Array.Copy(data, position, value, 0, length);
            position += length;
            return value;
        }

        private static int ReadLength(byte[] data, ref int position)
        {
            int length = data[position++];
            if ((length & 0x80) == 0) return length;

            var count = length & 0x7F;
            length = 0;
            for (var i = 0; i < count; i++) length = (length << 8) | data[position++];
            return length;
        }

        private static byte[] Pad32(byte[] value)
        {
            var start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            var length = value.Length - start;
            if (length > 32) throw new InvalidDataException("La clave de CB no es P-256.");

            var result = new byte[32];
            Array.Copy(value, start, result, 32 - length, length);
            return result;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static byte[] ToDer(byte[] signature)
        {
            if (signature.Length == 64)
            {
                var r = new byte[32];
                var s = new byte[32];
                Array.Copy(signature, 0, r, 0, 32);
                Array.Copy(signature, 32, s, 0, 32);

                var encodedR = EncodeInteger(r);
                var encodedS = EncodeInteger(s);
                var contentLength = encodedR.Length + encodedS.Length;
                var result = new byte[2 + contentLength];
                result[0] = 0x30;
                result[1] = (byte)contentLength;
                Array.Copy(encodedR, 0, result, 2, encodedR.Length);
                Array.Copy(encodedS, 0, result, 2 + encodedR.Length, encodedS.Length);
                return result;
            }

            if (signature.Length > 0 && signature[0] == 0x30) return signature;
            throw new InvalidDataException("No se pudo firmar la petición de CB.");
        }

        private static byte[] EncodeInteger(byte[] value)
        {
            var start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            var length = value.Length - start;
            var pad = (value[start] & 0x80) != 0;

            var result = new byte[2 + length + (pad ? 1 : 0)];
            result[0] = 0x02;
            result[1] = (byte)(length + (pad ? 1 : 0));
            if (pad) result[2] = 0;
            Array.Copy(value, start, result, pad ? 3 : 2, length);
            return result;
        }

        private void DisposeSigner()
        {
            if (signer != null)
            {
                try { signer.Dispose(); }
                catch { }
                signer = null;
            }
            publicKeyBase64 = null;
        }
    }
}
