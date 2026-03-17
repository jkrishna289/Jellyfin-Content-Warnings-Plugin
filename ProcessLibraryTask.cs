using Jellyfin.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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
        public string Description => "Tags movies and TV shows with CW: content warning tags using Groq AI. Skips already-tagged items.";
        public string Category => "Content Warnings";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }

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
                var movies = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true
                });
                items.AddRange(movies);
            }

            if (config.EnableTvShows)
            {
                var series = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Series },
                    Recursive = true
                });
                items.AddRange(series);
            }

            if (items.Count == 0)
            {
                _logger.LogInformation("[ContentWarnings] No items found.");
                progress.Report(100);
                return;
            }

            _logger.LogInformation("[ContentWarnings] Processing {Count} item(s).", items.Count);

            int done = 0;
            int tagged = 0;
            int skipped = 0;

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

                var result = await _groqClient.GetContentWarningsAsync(
                    item.Name, item.ProductionYear, cancellationToken)
                    .ConfigureAwait(false);

                if (result != null)
                {
                    await TagHelper.ApplyTagsAsync(
                        item, result, _libraryManager, _logger, cancellationToken)
                        .ConfigureAwait(false);
                    tagged++;
                }

                // Small delay to avoid rate-limiting Groq
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }

            progress.Report(100);
            _logger.LogInformation(
                "[ContentWarnings] Done. Tagged: {Tagged}, Already had tags: {Skipped}, Total: {Total}",
                tagged, skipped, items.Count);
        }
    }
}
