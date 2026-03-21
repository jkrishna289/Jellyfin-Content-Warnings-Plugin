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
        public List<SeriesGroupDto> Groups    { get; set; } = new();
        public List<ItemStatusDto>  Movies    { get; set; } = new();
        public DateTime             CachedAt  { get; set; }
        public bool IsExpired => false; // Only busted by writes
    }

    public class ItemStatusDto
    {
        public string       Id          { get; set; } = string.Empty;
        public string       Name        { get; set; } = string.Empty;
        public int?         Year        { get; set; }
        public string       Type        { get; set; } = string.Empty;
        public bool         HasWarnings { get; set; }
        public string       Rating      { get; set; } = string.Empty;
        public List<string> CwTags      { get; set; } = new();
    }

    public class SeriesGroupDto
    {
        public string            SeriesId   { get; set; } = string.Empty;
        public string            SeriesName { get; set; } = string.Empty;
        public int?              Year       { get; set; }
        public bool              HasWarnings { get; set; }
        public string            Rating     { get; set; } = string.Empty;
        public List<string>      CwTags     { get; set; } = new();
        public List<ItemStatusDto> Episodes { get; set; } = new();
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

        private static ItemStatusDto ToDto(BaseItem i, string type)
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
                Type        = type,
                HasWarnings = cwTags.Count > 0,
                Rating      = i.OfficialRating ?? string.Empty,
                CwTags      = cwTags
            };
        }

        // ── GET /ContentWarnings/LibraryStatus ────────────────────────
        [HttpGet("LibraryStatus")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetLibraryStatus([FromQuery] bool bustCache = false)
        {
            lock (_cacheLock)
            {
                if (!bustCache && _cache != null)
                    return Ok(new { cached = true, cachedAt = _cache.CachedAt, movies = _cache.Movies, series = _cache.Groups });
            }

            var config = Plugin.Instance?.Configuration;

            // Movies
            var movies = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true
            }).OrderBy(i => i.Name).Select(i => ToDto(i, "Movie")).ToList();

            // Series groups
            var seriesList = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true
            }).OrderBy(i => i.Name).ToList();

            var groups = new List<SeriesGroupDto>();
            foreach (var s in seriesList)
            {
                var sTags = s.Tags?
                    .Where(t => t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(t => t.Substring(TagHelper.Prefix.Length))
                    .ToList() ?? new List<string>();

                var group = new SeriesGroupDto
                {
                    SeriesId    = s.Id.ToString(),
                    SeriesName  = s.Name,
                    Year        = s.ProductionYear,
                    HasWarnings = sTags.Count > 0,
                    Rating      = s.OfficialRating ?? string.Empty,
                    CwTags      = sTags,
                    Episodes    = new List<ItemStatusDto>()
                };

                // Include episodes if episode tagging is enabled
                if (config?.EnableTvEpisodes == true)
                {
                    var episodes = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Episode },
                        AncestorIds      = new[] { s.Id },
                        Recursive        = true
                    }).OrderBy(e => e.Name).Select(e => ToDto(e, "Episode")).ToList();
                    group.Episodes = episodes;
                }

                groups.Add(group);
            }

            lock (_cacheLock)
            {
                _cache = new LibraryStatusCache { Movies = movies, Groups = groups, CachedAt = DateTime.UtcNow };
            }

            _logger.LogInformation("[ContentWarnings] LibraryStatus: {M} movies, {S} series.", movies.Count, groups.Count);
            return Ok(new { cached = false, cachedAt = DateTime.UtcNow, movies, series = groups });
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

            if (item.Tags != null)
            {
                item.Tags = item.Tags
                    .Where(t => !t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            var itemType = item.GetType().Name;
            var result = await _groqClient.GetContentWarningsAsync(
                item.Name, item.ProductionYear, itemType, CancellationToken.None).ConfigureAwait(false);

            if (result == null)
                return BadRequest(new { error = "Groq returned no result." });

            await TagHelper.ApplyTagsAsync(item, result, _libraryManager, _logger, CancellationToken.None)
                .ConfigureAwait(false);

            // Update cache in-place
            lock (_cacheLock)
            {
                if (_cache != null)
                {
                    var idStr = itemId.ToString();
                    var m = _cache.Movies.FirstOrDefault(x => x.Id == idStr);
                    if (m != null) { m.HasWarnings = result.Descriptors.Count > 0; m.CwTags = result.Descriptors; if (result.Rating.Length > 0) m.Rating = result.Rating; }
                    foreach (var g in _cache.Groups)
                    {
                        if (g.SeriesId == idStr) { g.HasWarnings = result.Descriptors.Count > 0; g.CwTags = result.Descriptors; if (result.Rating.Length > 0) g.Rating = result.Rating; break; }
                        var ep = g.Episodes.FirstOrDefault(x => x.Id == idStr);
                        if (ep != null) { ep.HasWarnings = result.Descriptors.Count > 0; ep.CwTags = result.Descriptors; if (result.Rating.Length > 0) ep.Rating = result.Rating; break; }
                    }
                }
            }

            return Ok(new { name = item.Name, rating = result.Rating, descriptors = result.Descriptors, reasoning = result.Reasoning });
        }

        // ── DELETE /ContentWarnings/ClearAll ──────────────────────────
        [HttpDelete("ClearAll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ClearAllTags(CancellationToken cancellationToken)
        {
            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
                Recursive = true
            });

            int cleared = 0;
            foreach (var item in items)
            {
                if (item.Tags == null) continue;
                if (!item.Tags.Any(t => t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase))) continue;
                item.Tags = item.Tags.Where(t => !t.StartsWith(TagHelper.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                await _libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                cleared++;
            }

            lock (_cacheLock)
            {
                if (_cache != null)
                {
                    foreach (var m in _cache.Movies) { m.HasWarnings = false; m.CwTags = new List<string>(); }
                    foreach (var g in _cache.Groups) { g.HasWarnings = false; g.CwTags = new List<string>(); foreach (var e in g.Episodes) { e.HasWarnings = false; e.CwTags = new List<string>(); } }
                }
            }

            _logger.LogInformation("[ContentWarnings] ClearAll: {Count} item(s) cleared.", cleared);
            return Ok(new { cleared, message = cleared + " item(s) had their CW: tags removed." });
        }
    }
}
