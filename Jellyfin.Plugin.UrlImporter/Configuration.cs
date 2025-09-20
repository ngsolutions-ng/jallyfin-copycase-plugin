using MediaBrowser.Model.Plugins;
using System.Collections.Generic;

namespace Jellyfin.Plugin.UrlImporter
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string DestinationFolder { get; set; } = string.Empty; // musi być folderem biblioteki
        public List<string> Urls { get; set; } = new();
        public bool OverwriteIfExists { get; set; } = false;
        public bool TriggerLibraryScan { get; set; } = true;

        // --- CopyCase ---
        public bool CopyCaseEnabled { get; set; } = false;
        public string? CopyCaseUsername { get; set; }
        public string? CopyCasePassword { get; set; }
        public string CopyCaseLoginUrl { get; set; } = "https://copycase.com/login"; // formularz logowania
        public string CopyCaseApiLoginUrl { get; set; } = "https://copycase.com/api/login"; // jeśli istnieje endpoint API
        public string CookieStoreFile { get; set; } = "copycase_cookies.json"; // plik z sesją
    }
}