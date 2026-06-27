using System.Net.Http.Json;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;

namespace CodeSage.Api.Services.Email;

// Sends email through Brevo's HTTP API (https://api.brevo.com/v3/smtp/email)
// using an API key. Works even on hosts that block outbound SMTP ports.
public class BrevoEmailSender : IEmailSender
{
    private readonly EmailSettings _s;
    private readonly IHttpClientFactory _httpFactory;

    public BrevoEmailSender(IOptions<EmailSettings> s, IHttpClientFactory httpFactory)
    {
        _s = s.Value;
        _httpFactory = httpFactory;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var client = _httpFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        req.Headers.Add("api-key", _s.ApiKey);
        req.Headers.Add("accept", "application/json");
        req.Content = JsonContent.Create(new
        {
            sender = new { email = _s.FromAddress, name = _s.FromName },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody,
        });

        var res = await client.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Brevo email send failed ({(int)res.StatusCode}): {body}");
        }
    }
}