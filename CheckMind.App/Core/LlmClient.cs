using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CheckMind.App.Core;

public sealed class LlmClient
{
    private readonly HttpClient _http;

    public LlmClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<JsonDocument> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "models");
        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task<string> ChatCompletionsAsync(string modelId, JsonElement messages, CancellationToken cancellationToken = default)
    {
        return await ChatCompletionsAsync(modelId, messages.GetRawText(), cancellationToken);
    }

    public async Task<string> ChatCompletionsAsync(string modelId, string messagesJson, CancellationToken cancellationToken = default)
    {
        var payload = $$"""
        {
          "model": "{{modelId}}",
          "messages": {{messagesJson}}
        }
        """;

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cancellationToken);
    }

    public static HttpClient CreateHttpClient(string baseUrl, string? apiKey)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        return http;
    }
}
