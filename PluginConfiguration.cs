using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentWarnings
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string GroqApiKey { get; set; } = string.Empty;

        public string GroqModel { get; set; } = "llama-3.3-70b-versatile";

        public bool EnableMovies { get; set; } = true;

        // Tags at series level — on by default
        public bool EnableTvShows { get; set; } = true;

        // Tags at episode level — OFF by default, costs many API calls
        public bool EnableTvEpisodes { get; set; } = false;
    }
}
