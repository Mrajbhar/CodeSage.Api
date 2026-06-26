namespace CodeSage.Api.Services.Email;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody);
}