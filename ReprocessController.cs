using System;
using System.Collections.Generic;
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
    public class LibraryStatusCache
    {
        public List<ItemStatusDto> Items { get; set; } = new();
        public DateTime CachedAt { get; set; }

        // Cache never expires by time — only busted by write operations
        public bool IsExpired => false;
    }

    public class ItemStatusDto
    {
        public string Id           { get; set; } = string.Empty;
        public string Name         { get; set; } = string.Empty;
        public int?   Year         { get; set; }
        public string Type         { get; set; } = string.Empty;
        public bool   HasWarnings  { get; set; }
        public string Rating       { get; set; } = string.Empty;
        public List<string> CwTags { get; set; } = new();
    }

    [ApiController]
    [Route("ContentWarnings")]
    [Authorize(Policy = "RequiresElevation")]
    [Produces(MediaTypeNames.Application.Json)]
    public class ReprocessController : ControllerBase
    {
        private readonly ILibraryManager _libraryManager;
        private readonly GroqClient _groqClient;
        private readonly ILogger<ReprocessController> _logger;

        // Static cache — only valid until a write operation clears it
        private static LibraryStatusCache? _cache;
        private static readonly object _cacheLock = new object();

        public ReprocessController(
            ILibraryManager libraryManager,
            GroqClient groqClient,
            ILogger<ReprocessController> logger)
        {
            _libraryManager = libraryManager;
            _groqClient = groqClient;
            _logger = logger;
        }

        // ── GET /ContentWarnings/LibraryStatus ────────────────────────
        [HttpGet("LibraryStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetLibraryStatus([FromQuery] bool bustCache = false)
        {
            lock (_cacheLock)
            {
                if (!bustCache && _cache != null)
                {
                    _logger.LogDebug("[ContentWarnings] Returning cached library status ({Count} items)", _cache.Items.Count);
                    return Ok(new { cached = true, cachedAt = _cache.CachedAt, items = _cache.Items });
                }
            }

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            });

            var dtos = items
                .OrderBy(i => i.Name)
                .Select(i =>
                {
                    var cwTags = i.Tags?
                        .Where(t => t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(t => t.Substring(TagHelper.Prefix.Length))
                        .ToList() ?? new List<string>();

                    return new ItemStatusDto
                    {
                        Id          = i.Id.ToString(),
                        Name        = i.Name,
                        Year        = i.ProductionYear,
                        Type        = i is MediaBrowser.Controller.Entities.TV.Series ? "Series" : "Movie",
                        HasWarnings = cwTags.Count > 0,
                        Rating      = i.OfficialRating ?? string.Empty,
                        CwTags      = cwTags
                    };
                })
                .ToList();

            lock (_cacheLock)
            {
                _cache = new LibraryStatusCache { Items = dtos, CachedAt = DateTime.UtcNow };
            }

            _logger.LogInformation("[ContentWarnings] LibraryStatus: fetched and cached {Count} items.", dtos.Count);
            return Ok(new { cached = false, cachedAt = DateTime.UtcNow, items = dtos });
        }

        // ── POST /ContentWarnings/Reprocess/{itemId} ──────────────────
        [HttpPost("Reprocess/{itemId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ReprocessItem([FromRoute] Guid itemId)
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null)
                return NotFound(new { error = "Item not found." });

            _logger.LogInformation("[ContentWarnings] Manual reprocess: '{Name}' ({Id})", item.Name, itemId);

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

            await TagHelper.ApplyTagsAsync(item, result, _libraryManager, _logger, CancellationToken.None)
                .ConfigureAwait(false);

            // Update cache in-place so the table row reflects instantly
            lock (_cacheLock)
            {
                if (_cache != null)
                {
                    var cached = _cache.Items.FirstOrDefault(i => i.Id == itemId.ToString());
                    if (cached != null)
                    {
                        cached.HasWarnings = result.Descriptors.Count > 0;
                        cached.CwTags      = result.Descriptors;
                        cached.Rating      = string.IsNullOrWhiteSpace(result.Rating) ? cached.Rating : result.Rating;
                    }
                }
            }

            return Ok(new
            {
                name        = item.Name,
                rating      = result.Rating,
                descriptors = result.Descriptors,
                reasoning   = result.Reasoning,
                tags        = item.Tags
            });
        }

        // ── DELETE /ContentWarnings/ClearAll ──────────────────────────
        [HttpDelete("ClearAll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ClearAllTags(CancellationToken cancellationToken)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            });

            int cleared = 0;
            foreach (var item in items)
            {
                if (item.Tags == null) continue;
                if (!item.Tags.Any(t => t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))) continue;

                item.Tags = item.Tags
                    .Where(t => !t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                await _libraryManager.UpdateItemAsync(item, item.GetParent(),
                    ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                cleared++;
            }

            // Update cache in-place — clear all CW data
            lock (_cacheLock)
            {
                if (_cache != null)
                {
                    foreach (var cached in _cache.Items)
                    {
                        cached.HasWarnings = false;
                        cached.CwTags      = new List<string>();
                    }
                }
            }

            _logger.LogInformation("[ContentWarnings] ClearAll: removed CW: tags from {Count} item(s).", cleared);
            return Ok(new { cleared, message = cleared + " item(s) had their CW: tags removed." });
        }
    }
}
