using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;

namespace CodeSage.Api.Services;

// Thin client over the Razorpay REST API.
// Avoids the Razorpay.NET SDK so we have no extra NuGet dependency and we control the contract.
// Docs: https://razorpay.com/docs/api/payments/subscriptions/
public class RazorpayClient
{
    private readonly IHttpClientFactory _http;
    private readonly BillingSettings _b;

    public RazorpayClient(IHttpClientFactory http, IOptions<BillingSettings> b)
    {
        _http = http;
        _b = b.Value;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_b.KeyId) && !string.IsNullOrWhiteSpace(_b.KeySecret);

    public string KeyId => _b.KeyId;

    // Creates a recurring subscription against an existing plan_id from the dashboard.
    // total_count of 120 = 10 years of monthly billing; Razorpay requires it.
    public async Task<RazorpaySubscription> CreateSubscriptionAsync(string planId, string orgId)
    {
        var client = Authed();
        var body = new
        {
            plan_id = planId,
            customer_notify = 1,
            total_count = 120,
            notes = new { org_id = orgId }
        };

        var resp = await client.PostAsJsonAsync("https://api.razorpay.com/v1/subscriptions", body);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Razorpay {(int)resp.StatusCode} {resp.StatusCode}: {detail}");
        }
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return new RazorpaySubscription(
            doc.GetProperty("id").GetString()!,
            doc.GetProperty("status").GetString()!,
            doc.TryGetProperty("short_url", out var u) ? u.GetString() : null);
    }

    public async Task CancelSubscriptionAsync(string subscriptionId)
    {
        var client = Authed();
        var resp = await client.PostAsJsonAsync(
            $"https://api.razorpay.com/v1/subscriptions/{subscriptionId}/cancel",
            new { cancel_at_cycle_end = 0 });
        resp.EnsureSuccessStatusCode();
    }

    // Razorpay signs every webhook with HMAC-SHA256 over the raw request body
    // using your WebhookSecret. We compare the hex digest against the X-Razorpay-Signature header.
    public bool VerifyWebhookSignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(_b.WebhookSecret))
            return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_b.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hex),
            Encoding.ASCII.GetBytes(signatureHeader));
    }

    private HttpClient Authed()
    {
        var c = _http.CreateClient();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_b.KeyId}:{_b.KeySecret}"));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return c;
    }
}

public record RazorpaySubscription(string Id, string Status, string? ShortUrl);