using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Infrastructure.Email;

public class EmailSender
{
    private readonly IConfiguration configuration;

    public EmailSender(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public async Task SendEmailAsync(string userEmail, string emailSubject, string msg)
    {
        var client = new SendGridClient(configuration["Sendgrid:Key"]);
        var message = new SendGridMessage
        {
            From = new EmailAddress("maviek14@wp.pl", configuration["Sendgrid:User"]),
            Subject = emailSubject,
            PlainTextContent = msg,
            HtmlContent = msg
        };
        message.AddTo(new EmailAddress(userEmail));
        message.SetClickTracking(false, false);

        await client.SendEmailAsync(message);
    }
}
