using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ContentWarnings
{
    public class ServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<GroqClient>();
            serviceCollection.AddHostedService<ContentWarningProvider>();
            serviceCollection.AddSingleton<IScheduledTask, ProcessLibraryTask>();
        }
    }
}
