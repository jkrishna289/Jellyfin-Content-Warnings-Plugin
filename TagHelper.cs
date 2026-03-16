using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings
{
    public static class TagHelper
    {
        public const string Prefix = "CW:";

        public static bool HasContentWarningTags(BaseItem item)
        {
            return item.Tags != null &&
                   item.Tags.Any(t => t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));
        }

        public static async Task ApplyTagsAsync(
            BaseItem item,
            ContentWarningResult result,
            ILibraryManager libraryManager,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var existing = item.Tags?.ToList() ?? new List<string>();

            // Remove old CW: tags
            var kept = existing
                .Where(t => !t.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Add new CW: tags
            var newTags = result.Descriptors
                .Select(d => Prefix + d)
                .ToList();

            item.Tags = kept.Concat(newTags).ToArray();

            // Set official rating only if not already set
            if (!string.IsNullOrWhiteSpace(result.Rating) &&
                string.IsNullOrWhiteSpace(item.OfficialRating))
            {
                item.OfficialRating = result.Rating;
            }

            await libraryManager.UpdateItemAsync(
                item,
                item.GetParent(),
                ItemUpdateType.MetadataEdit,
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation("[ContentWarnings] Tagged '{Name}': {Tags}",
                item.Name, string.Join(", ", newTags));
        }
    }
}
