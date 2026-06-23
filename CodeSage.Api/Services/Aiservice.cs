using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeSage.Api.Dtos;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;

namespace CodeSage.Api.Services;

// Talks to any OpenAI-compatible /chat/completions endpoint.
// OpenAI: BaseUrl https://api.openai.com/v1 + real key.
// Ollama: BaseUrl http://localhost:11434/v1 + any key + a local model name.
public class AiService
{
    private readonly IHttpClientFactory _http;
    private readonly AiSettings _ai;

    public AiService(IHttpClientFactory http, IOptions<AiSettings> ai)
    {
        _http = http;
        _ai = ai.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_ai.ApiKey) || _ai.BaseUrl.Contains("localhost");

    public async Task<string> ChatAsync(string system, string user)
    {
        var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);   // local models on CPU can be slow

        var req = new HttpRequestMessage(HttpMethod.Post, $"{_ai.BaseUrl.TrimEnd('/')}/chat/completions");
        if (!string.IsNullOrWhiteSpace(_ai.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ai.ApiKey);

        req.Content = JsonContent.Create(new
        {
            model = _ai.Model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        });

        var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"AI {(int)resp.StatusCode} {resp.StatusCode} @ {_ai.BaseUrl} model={_ai.Model}: {detail}");
        }

        var doc = await resp.Content.ReadFromJsonAsync<ChatResponse>();
        return doc?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
               ?? "No response from the model.";
    }

    public Task<string> ExplainCodeAsync(string code, string? path, string? language)
    {
        var system =
            "You are a senior software engineer reviewing code for a teammate. " +
            "Explain clearly and concisely what the given code does, its key responsibilities, " +
            "notable logic, and any risks or smells worth attention. Use short paragraphs and bullet points. " +
            "Do not repeat the code back verbatim.";

        var header = path is not null ? $"File: {path}\n" : "";
        if (language is not null) header += $"Language: {language}\n";

        return ChatAsync(system, $"{header}\nCode:\n```\n{code}\n```");
    }

    // ---- Phase 2 #4: structured AI review of a pull-request diff ----
    public async Task<ReviewResultDto> ReviewDiffAsync(string diff, string? title)
    {
        var system =
            "You are a senior code reviewer. Review the unified diff for bugs, security issues, risky changes, " +
            "and clear style problems. Respond with ONLY a JSON object (no markdown fences) shaped exactly as: " +
            "{\"summary\": string, \"comments\": [{\"file\": string|null, \"severity\": \"info\"|\"warning\"|\"critical\", \"comment\": string}]}. " +
            "Make each comment specific and actionable. If the change looks fine, return an empty comments array and a brief positive summary.";

        var user = (title is not null ? $"Pull request: {title}\n\n" : "") + "Diff:\n" + diff;
        var raw = await ChatAsync(system, user);
        return ParseReview(raw);
    }

    private static ReviewResultDto ParseReview(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            var nl = text.IndexOf('\n');
            if (nl >= 0) text = text[(nl + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
            text = text.Trim();
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            var comments = new List<ReviewCommentDto>();
            if (root.TryGetProperty("comments", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in arr.EnumerateArray())
                {
                    var file = c.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                    var sev = c.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "info" : "info";
                    var cm = c.TryGetProperty("comment", out var cc) ? cc.GetString() ?? "" : "";
                    comments.Add(new ReviewCommentDto(file, sev, cm));
                }
            }
            return new ReviewResultDto(summary, comments);
        }
        catch
        {
            // model didn't return valid JSON — surface its text as the summary
            return new ReviewResultDto(raw, new List<ReviewCommentDto>());
        }
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }
    private class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }
    private class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}