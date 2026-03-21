using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings
{
    public class ContentWarningProvider : IHostedService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly GroqClient _groqClient;
        private readonly ILogger<ContentWarningProvider> _logger;

        public ContentWarningProvider(
            ILibraryManager libraryManager,
            GroqClient groqClient,
            ILogger<ContentWarningProvider> logger)
        {
            _libraryManager = libraryManager;
            _groqClient = groqClient;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded += OnItemAdded;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            return Task.CompletedTask;
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            var item = e.Item;
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            bool shouldProcess =
                (item is Movie && config.EnableMovies) ||
                (item is Series && config.EnableTvShows && !config.EnableTvEpisodes) ||
                (item is Episode && config.EnableTvEpisodes);

            if (!shouldProcess) return;
            if (TagHelper.HasContentWarningTags(item)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var itemType = item.GetType().Name;
                    var result = await _groqClient.GetContentWarningsAsync(
                        item.Name, item.ProductionYear, itemType, CancellationToken.None)
                        .ConfigureAwait(false);

                    if (result != null)
                    {
                        await TagHelper.ApplyTagsAsync(
                            item, result, _libraryManager, _logger, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ContentWarnings] Failed to process '{Name}'", item.Name);
                }
            });
        }
    }
}
