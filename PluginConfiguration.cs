using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentWarnings
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string GroqApiKey { get; set; } = string.Empty;

        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";

        public bool EnableMovies { get; set; } = true;

        public bool EnableTvShows { get; set; } = true;
    }
}