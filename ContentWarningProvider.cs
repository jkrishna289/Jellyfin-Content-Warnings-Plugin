using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings;

/// <summary>
/// Prefix applied to all auto-generated content warning tags.
/// This keeps them separate from any manually added tags.
/// </summary>
public static class TagPrefix
{
    public const string ContentWarning = "CW:";
}

/// <summary>
/// Runs after each library scan and tags unprocessed items with content warnings.
/// </summary>
public class ContentWarningProvider : IServerEntryPoint
{
    private readonly ILibraryManager _libraryManager;
    private readonly GroqClient _groqClient;
    private readonly ILogger<ContentWarningProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentWarningProvider"/> class.
    /// </summary>
    public ContentWarningProvider(
        ILibraryManager libraryManager,
        GroqClient groqClient,
        ILogger<ContentWarningProvider> logger)
    {
        _libraryManager = libraryManager;
        _groqClient = groqClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task RunAsync()
    {
        // Hook into the library scan completion event
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Triggered when a new item is added to the library.
    /// </summary>
    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        _ = ProcessItemAsync(e.Item, CancellationToken.None);
    }

    /// <summary>
    /// Triggered when an existing item is updated (e.g. after metadata refresh).
    /// Only processes if item has no CW tags yet.
    /// </summary>
    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;

        // Skip if already tagged — don't overwrite
        if (HasContentWarningTags(item))
        {
            return;
        }

        _ = ProcessItemAsync(item, CancellationToken.None);
    }

    /// <summary>
    /// Core logic: fetch warnings from Groq and save them to the item.
    /// </summary>
    private async Task ProcessItemAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null) return;

        // Check item type against config
        bool shouldProcess = item switch
        {
            Movie  => config.EnableMovies,
            Series => config.EnableTvShows,
            _      => false
        };

        if (!shouldProcess) return;

        // Skip if already has CW tags
        if (HasContentWarningTags(item))
        {
            _logger.LogDebug("[ContentWarnings] Skipping '{Name}' — already tagged.", item.Name);
            return;
        }

        _logger.LogInformation("[ContentWarnings] Processing '{Name}' ({Year})", item.Name, item.ProductionYear);

        var result = await _groqClient.GetContentWarningsAsync(
            item.Name,
            item.ProductionYear,
            cancellationToken).ConfigureAwait(false);

        if (result is null) return;

        // Build new tag list: keep existing non-CW tags, add new CW: tags
        var existingTags = item.Tags?.ToList() ?? new List<string>();
        var nonCwTags = existingTags
            .Where(t => !t.StartsWith(TagPrefix.ContentWarning, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var newCwTags = result.Descriptors
            .Select(d => $"{TagPrefix.ContentWarning}{d}")
            .ToList();

        item.Tags = nonCwTags.Concat(newCwTags).ToArray();

        // Also save the official MPAA rating if not already set
        if (!string.IsNullOrWhiteSpace(result.Rating) &&
            string.IsNullOrWhiteSpace(item.OfficialRating))
        {
            item.OfficialRating = result.Rating;
        }

        // Persist to Jellyfin DB
        await _libraryManager.UpdateItemAsync(
            item,
            item.GetParent(),
            ItemUpdateType.MetadataEdit,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "[ContentWarnings] Tagged '{Name}' with: {Tags}",
            item.Name,
            string.Join(", ", newCwTags));
    }

    /// <summary>
    /// Returns true if the item already has at least one CW: tag.
    /// </summary>
    private static bool HasContentWarningTags(BaseItem item)
    {
        return item.Tags?.Any(t =>
            t.StartsWith(TagPrefix.ContentWarning, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        GC.SuppressFinalize(this);
    }
}
