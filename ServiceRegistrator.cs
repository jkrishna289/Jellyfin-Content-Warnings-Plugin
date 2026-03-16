using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ContentWarnings;

/// <summary>
/// Registers plugin services with Jellyfin's dependency injection container.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<GroqClient>();
        serviceCollection.AddSingleton<IServerEntryPoint, ContentWarningProvider>();
    }
}
