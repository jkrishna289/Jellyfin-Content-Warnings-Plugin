using Jellyfin.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings
{
    public class ProcessLibraryTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly GroqClient _groqClient;
        private readonly ILogger<ProcessLibraryTask> _logger;

        public ProcessLibraryTask(
            ILibraryManager libraryManager,
            GroqClient groqClient,
            ILogger<ProcessLibraryTask> logger)
        {
            _libraryManager = libraryManager;
            _groqClient = groqClient;
            _logger = logger;
        }

        public string Name => "Process Content Warnings";
        public string Key => "ContentWarningsProcessLibrary";
        public string Description => "Tags movies and TV shows/episodes with CW: content warning tags using Groq AI. Skips already-tagged items.";
        public string Category => "Content Warnings";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
            => Array.Empty<TaskTriggerInfo>();

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.GroqApiKey))
            {
                _logger.LogWarning("[ContentWarnings] Task aborted: no Groq API key.");
                return;
            }

            var items = new List<BaseItem>();

            if (config.EnableMovies)
            {
                items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true
                }));
            }

            // If episode tagging is enabled, tag episodes only (not series)
            // If only series tagging is enabled, tag at series level
            if (config.EnableTvEpisodes)
            {
                _logger.LogInformation("[ContentWarnings] Episode tagging enabled — tagging individual episodes.");
                items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    Recursive = true
                }));
            }
            else if (config.EnableTvShows)
            {
                items.AddRange(_libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    Recursive = true
                }));
            }

            if (items.Count == 0)
            {
                _logger.LogInformation("[ContentWarnings] No items found.");
                progress.Report(100);
                return;
            }

            _logger.LogInformation("[ContentWarnings] Processing {Count} item(s).", items.Count);

            int done = 0, tagged = 0, skipped = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report((double)done / items.Count * 100.0);
                done++;

                if (TagHelper.HasContentWarningTags(item))
                {
                    skipped++;
                    continue;
                }

                var itemType = item.GetType().Name;
                var result = await _groqClient.GetContentWarningsAsync(
                    item.Name, item.ProductionYear, itemType, cancellationToken)
                    .ConfigureAwait(false);

                if (result != null)
                {
                    await TagHelper.ApplyTagsAsync(item, result, _libraryManager, _logger, cancellationToken)
                        .ConfigureAwait(false);
                    tagged++;
                }

                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(100);
            _logger.LogInformation(
                "[ContentWarnings] Done. Tagged: {Tagged}, Skipped: {Skipped}, Total: {Total}",
                tagged, skipped, items.Count);
        }
    }
}
