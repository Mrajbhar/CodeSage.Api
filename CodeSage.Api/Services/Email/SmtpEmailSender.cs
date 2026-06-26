using System.Net;
using System.Net.Mail;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;

namespace CodeSage.Api.Services.Email;

// Production sender using the built-in System.Net.Mail (no extra NuGet package).
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _s;
    public SmtpEmailSender(IOptions<EmailSettings> s) => _s = s.Value;

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        using var msg = new MailMessage
        {
            From = new MailAddress(_s.FromAddress, _s.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        msg.To.Add(toEmail);

        using var client = new SmtpClient(_s.Host, _s.Port)
        {
            EnableSsl = _s.UseSsl,
            Credentials = new NetworkCredential(_s.Username, _s.Password),
        };
        await client.SendMailAsync(msg);
    }
}