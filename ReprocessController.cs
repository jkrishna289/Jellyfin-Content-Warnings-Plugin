using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings
{
    [ApiController]
    [Route("ContentWarnings")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces(MediaTypeNames.Application.Json)]
    public class ReprocessController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly GroqClient _groqClient;
        private readonly ILogger<ReprocessController> _logger;

        public ReprocessController(
            ILibraryManager libraryManager,
            GroqClient groqClient,
            ILogger<ReprocessController> logger)
        {
            _libraryManager = libraryManager;
            _groqClient = groqClient;
            _logger = logger;
        }

        /// <summary>
        /// Re-processes content warnings for a single item.
        /// Clears existing CW: tags and re-fetches from Groq.
        /// </summary>
        [HttpPost("Reprocess/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReprocessItem([FromRoute] Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
            {
                _logger.LogWarning("[ContentWarnings] Reprocess: item {Id} not found.", itemId);
                return NotFound(new { error = "Item not found." });
            }

            _logger.LogInformation(
                "[ContentWarnings] Manual reprocess requested for '{Name}' ({Id})",
                item.Name, itemId);

            // Strip existing CW: tags
            if (item.Tags != null)
            {
                item.Tags = item.Tags
                    .Where(t => !t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            var itemType = item.GetType().Name;
            var result = await _groqClient.GetContentWarningsAsync(
                item.Name, item.ProductionYear, itemType, CancellationToken.None)
                .ConfigureAwait(false);

            if (result == null)
                return BadRequest(new { error = "Groq returned no result. Check your API key and logs." });

            await TagHelper.ApplyTagsAsync(
                item, result, _libraryManager, _logger, CancellationToken.None)
                .ConfigureAwait(false);

            return Ok(new
            {
                name        = item.Name,
                rating      = result.Rating,
                descriptors = result.Descriptors,
                reasoning   = result.Reasoning,
                tags        = item.Tags
            });
        }

        /// <summary>
        /// Removes ALL CW: tags from every item in the library.
        /// </summary>
        [HttpDelete("ClearAll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ClearAllTags(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[ContentWarnings] ClearAll: starting removal of all CW: tags.");

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive        = true
            });

            int cleared = 0;

            foreach (var item in items)
            {
                if (item.Tags == null) continue;

                var hasCw = item.Tags.Any(t =>
                    t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase));

                if (!hasCw) continue;

                item.Tags = item.Tags
                    .Where(t => !t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                await _libraryManager.UpdateItemAsync(
                    item,
                    item.GetParent(),
                    ItemUpdateType.MetadataEdit,
                    cancellationToken).ConfigureAwait(false);

                cleared++;
            }

            _logger.LogInformation("[ContentWarnings] ClearAll: removed CW: tags from {Count} item(s).", cleared);

            return Ok(new { cleared, message = cleared + " item(s) had their CW: tags removed." });
        }
    }
}
