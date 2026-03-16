using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentWarnings;

/// <summary>
/// Response from Groq containing rating and descriptors.
/// </summary>
public class ContentWarningResult
{
    public string Rating { get; set; } = string.Empty;
    public List<string> Descriptors { get; set; } = new();
}

/// <summary>
/// Handles communication with the Groq API to fetch content descriptors.
/// </summary>
public class GroqClient
{
    private const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";

    private static readonly string[] KnownDescriptors =
    {
        "Violence", "Graphic Violence", "Gore",
        "Language", "Strong Language", "Profanity",
        "Sexual Content", "Nudity", "Sexual Violence",
        "Drug Use", "Alcohol Use", "Smoking",
        "Frightening Scenes", "Disturbing Content",
        "Gambling", "Self-Harm", "Suicide",
        "Racism", "Discrimination", "Fantasy Violence"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GroqClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GroqClient"/> class.
    /// </summary>
    public GroqClient(IHttpClientFactory httpClientFactory, ILogger<GroqClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Queries Groq for content warnings for the given title.
    /// </summary>
    /// <param name="title">The movie or show title.</param>
    /// <param name="year">The release year (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ContentWarningResult"/> or null on failure.</returns>
    public async Task<ContentWarningResult?> GetContentWarningsAsync(
        string title,
        int? year,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.GroqApiKey))
        {
            _logger.LogWarning("[ContentWarnings] Groq API key is not configured.");
            return null;
        }

        var descriptorList = string.Join(", ", KnownDescriptors);
        var titleWithYear = year.HasValue ? $"{title} ({year})" : title;

        var systemPrompt = $"""
            You are a film content classifier.
            Given a movie or TV show title, return ONLY a JSON object with exactly these two fields:
            {{
              "rating": "<official MPAA or TV rating e.g. R, PG-13, TV-MA, PG, G>",
              "descriptors": ["<descriptor1>", "<descriptor2>"]
            }}
            Choose descriptors ONLY from this list: {descriptorList}
            Return an empty array for descriptors if none apply.
            Never add descriptors not in the list. No explanation, no markdown, only JSON.
            """;

        var requestBody = new
        {
            model = config.GroqModel,
            temperature = 0.1,
            max_tokens = 256,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = $"\"{titleWithYear}\"" }
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.GroqApiKey);

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(GroqApiUrl, content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[ContentWarnings] Groq API error {StatusCode} for '{Title}'",
                    response.StatusCode, title);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // Parse the OpenAI-compatible response envelope
            using var doc = JsonDocument.Parse(responseBody);
            var messageContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                _logger.LogWarning("[ContentWarnings] Empty response from Groq for '{Title}'", title);
                return null;
            }

            // Strip accidental markdown fences
            messageContent = messageContent.Trim();
            if (messageContent.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = messageContent.IndexOf('\n');
                var lastFence = messageContent.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                {
                    messageContent = messageContent[(firstNewline + 1)..lastFence].Trim();
                }
            }

            var result = JsonSerializer.Deserialize<ContentWarningResult>(messageContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result is null)
            {
                _logger.LogWarning("[ContentWarnings] Failed to deserialize Groq response for '{Title}'", title);
                return null;
            }

            _logger.LogInformation(
                "[ContentWarnings] Got {Count} descriptor(s) for '{Title}': {Descriptors}",
                result.Descriptors.Count, title, string.Join(", ", result.Descriptors));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ContentWarnings] Exception querying Groq for '{Title}'", title);
            return null;
        }
    }
}
