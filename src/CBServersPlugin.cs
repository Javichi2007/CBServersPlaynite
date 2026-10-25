using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace CBServersPlaynite
{
    public class CBServersPlugin : GenericPlugin
    {
        public static readonly Guid PluginId = Guid.Parse("b7f3c1e2-5a4d-4c8e-9f21-8d6a3c2b1f70");

        private readonly IPlayniteAPI api;
        private readonly LauncherService service;

        public override Guid Id
        {
            get { return PluginId; }
        }

        public CBServersPlugin(IPlayniteAPI api) : base(api)
        {
            this.api = api;
            var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            service = new LauncherService(root, GetPluginUserDataPath());
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Type = SiderbarItemType.View,
                Title = "CB Servers",
                Icon = GetIconPath(),
                Opened = () => new CBServersView(service, api)
            };
        }

        private static string GetIconPath()
        {
            var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Assets", "cb-icon.png");
            return File.Exists(path) ? path : null;
        }
    }
}
