using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;

namespace CodeSage.Api.Services;

// Phase 4 #4: turns text into vectors using the configured embedding model (e.g. Ollama's nomic-embed-text).
public class EmbeddingService
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettings _ai;
    public EmbeddingService(IHttpClientFactory http, IOptions<AiSettings> ai) { _http = http; _ai = ai.Value; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_ai.BaseUrl) && !string.IsNullOrWhiteSpace(_ai.EmbedModel);

    public async Task<float[]> EmbedAsync(string text)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);

        var req = new HttpRequestMessage(HttpMethod.Post, $"{_ai.BaseUrl.TrimEnd('/')}/embeddings");
        if (!string.IsNullOrWhiteSpace(_ai.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ai.ApiKey);
        req.Content = JsonContent.Create(new { model = _ai.EmbedModel, input = text });

        var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Embedding {(int)resp.StatusCode}: {detail}");
        }

        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var arr = doc.GetProperty("data")[0].GetProperty("embedding");
        var vec = new float[arr.GetArrayLength()];
        for (int i = 0; i < vec.Length; i++) vec[i] = arr[i].GetSingle();
        return vec;
    }
}