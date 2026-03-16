using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentWarnings;

/// <summary>
/// Plugin configuration — stored in Jellyfin and shown on the admin dashboard.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Groq API key entered by the admin.
    /// </summary>
    public string GroqApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Groq model to use.
    /// </summary>
    public string GroqModel { get; set; } = "llama3-70b-8192";

    /// <summary>
    /// Gets or sets a value indicating whether to tag movies.
    /// </summary>
    public bool EnableMovies { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to tag TV shows.
    /// </summary>
    public bool EnableTvShows { get; set; } = true;
}
