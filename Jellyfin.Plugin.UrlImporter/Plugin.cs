using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using System;

namespace Jellyfin.Plugin.UrlImporter
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "URL Importer";
        public override string Description => "Pobieranie plików z URL (w tym z copycase.com po zalogowaniu) do folderu biblioteki.";

        public static Plugin? Instance { get; private set; }

        public PluginPageInfo[] GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name = "urlimporter",
                EmbeddedResourcePath = GetType().Namespace + ".Web.config.html"
            }
        };
    }
}