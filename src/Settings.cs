using System;
using System.IO;
using System.Web.Script.Serialization;

namespace CBServersPlaynite
{
    public class PluginSettings
    {
        public string LauncherPath { get; set; }
        public string ServerGameId { get; set; }

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static PluginSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = Json.Deserialize<PluginSettings>(File.ReadAllText(path));
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
            }

            return new PluginSettings();
        }

        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, Json.Serialize(this));
            }
            catch
            {
            }
        }
    }
}
