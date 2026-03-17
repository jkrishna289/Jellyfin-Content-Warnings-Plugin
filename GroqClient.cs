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

namespace Jellyfin.Plugin.ContentWarnings
{
    public class ContentWarningResult
    {
        [JsonPropertyName("rating")]
        public string Rating { get; set; } = string.Empty;

        [JsonPropertyName("descriptors")]
        public List<string> Descriptors { get; set; } = new List<string>();
    }

    public class GroqClient
    {
        private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";

        private static readonly string[] KnownDescriptors = new[]
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

        public GroqClient(IHttpClientFactory httpClientFactory, ILogger<GroqClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ContentWarningResult?> GetContentWarningsAsync(
            string title,
            int? year,
            CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.GroqApiKey))
            {
                _logger.LogWarning("[ContentWarnings] No Groq API key configured.");
                return null;
            }

            // Trim key to remove any accidental whitespace/newlines from config
            var apiKey = config.GroqApiKey.Trim();
            var model = (config.GroqModel ?? "llama3-70b-8192").Trim();

            var descriptorList = string.Join(", ", KnownDescriptors);
            var titleWithYear = year.HasValue ? title + " (" + year.Value + ")" : title;

            var systemPrompt =
                "You are a film content classifier. " +
                "Given a movie or TV show title, return ONLY a JSON object with exactly these two fields: " +
                "{\"rating\": \"<MPAA or TV rating e.g. R, PG-13, TV-MA, PG, G>\", \"descriptors\": [\"<descriptor1>\"]} " +
                "Choose descriptors ONLY from this list: " + descriptorList + " " +
                "Return an empty array if none apply. No explanation, no markdown, only raw JSON.";

            var requestBody = new
            {
                model = model,
                temperature = 0.1,
                max_tokens = 256,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = titleWithYear }
                }
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("[ContentWarnings] Sending request for '{Title}' using model '{Model}'", title, model);

                var response = await client.PostAsync(ApiUrl, httpContent, cancellationToken)
                    .ConfigureAwait(false);

                // Always read the body so we can log errors properly
                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[ContentWarnings] Groq returned {Code} for '{Title}'. Response: {Body}",
                        response.StatusCode, title, body);
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var messageContent = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(messageContent))
                {
                    _logger.LogWarning("[ContentWarnings] Empty Groq response for '{Title}'", title);
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
                        messageContent = messageContent.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                    }
                }

                var result = JsonSerializer.Deserialize<ContentWarningResult>(messageContent);
                if (result == null)
                {
                    _logger.LogWarning("[ContentWarnings] Failed to parse Groq response for '{Title}': {Raw}", title, messageContent);
                    return null;
                }

                _logger.LogInformation("[ContentWarnings] '{Title}' -> {Rating} | {Descriptors}",
                    title, result.Rating, string.Join(", ", result.Descriptors));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ContentWarnings] Exception for '{Title}'", title);
                return null;
            }
        }
    }
}