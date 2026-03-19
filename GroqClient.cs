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

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = string.Empty;
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
            string itemType,
            CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.GroqApiKey))
            {
                _logger.LogWarning("[ContentWarnings] No Groq API key configured.");
                return null;
            }

            var apiKey = config.GroqApiKey.Trim();
            var model = (config.GroqModel ?? "llama-3.3-70b-versatile").Trim();
            var descriptorList = string.Join(", ", KnownDescriptors);
            var titleWithYear = year.HasValue ? title + " (" + year.Value + ")" : title;
            var typeLabel = itemType == "Series" ? "TV series" : "movie";

            var systemPrompt =
                "You are an expert film and television content classifier with deep knowledge of rating systems worldwide. " +
                "Given a " + typeLabel + " title, return ONLY a JSON object with exactly these three fields: " +
                "{\"rating\": \"<official rating>\", \"descriptors\": [\"<descriptor>\"], \"reasoning\": \"<brief reason>\"} " +
                "For 'rating': use the most widely known official rating for this title (e.g. R, PG-13, TV-MA, 15, 18). " +
                "If the title is unknown to you, make your best educated guess based on the genre implied by the title. " +
                "For 'descriptors': choose ONLY from this exact list: " + descriptorList + ". " +
                "Pick all that genuinely apply — do not add descriptors not in the list. " +
                "Return an empty array only if truly no descriptors apply. " +
                "For 'reasoning': write 1-2 sentences explaining why you chose those descriptors and rating. " +
                "No markdown, no extra text, only raw JSON.";

            var userMessage =
                "Title: \"" + titleWithYear + "\" " +
                "Type: " + typeLabel;

            var requestBody = new
            {
                model = model,
                temperature = 0.1,
                max_tokens = 400,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(ApiUrl, httpContent, cancellationToken)
                    .ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[ContentWarnings] Groq returned {Code} for '{Title}'. Body: {Body}",
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
                        messageContent = messageContent.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }

                var result = JsonSerializer.Deserialize<ContentWarningResult>(messageContent);
                if (result == null)
                {
                    _logger.LogWarning("[ContentWarnings] Failed to parse response for '{Title}': {Raw}", title, messageContent);
                    return null;
                }

                // Log reasoning for debugging
                _logger.LogInformation(
                    "[ContentWarnings] '{Title}' → {Rating} | {Descriptors} | Reason: {Reasoning}",
                    title,
                    result.Rating,
                    string.Join(", ", result.Descriptors),
                    result.Reasoning);

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
