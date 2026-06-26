using Microsoft.Extensions.Logging;

namespace CodeSage.Api.Services.Email;

// Dev sender: writes the email to the API logs instead of sending it.
// You'll see invite/reset links right in your console — no SMTP needed.
public class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _log;
    public ConsoleEmailSender(ILogger<ConsoleEmailSender> log) => _log = log;

    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        _log.LogInformation("──────────── EMAIL (dev) ────────────\nTo: {To}\nSubject: {Subject}\n{Body}\n─────────────────────────────────────",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}