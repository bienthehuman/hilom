using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Hilom.Infrastructure.Email;

/// <summary>One email to one person, with a plain-text body and an HTML version.</summary>
public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    /// <summary>Sends now. Throws <see cref="EmailNotSentException"/> when the mail server can't take it.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class EmailNotSentException(string message, Exception inner) : Exception(message, inner);

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(settings.SmtpHost, settings.SmtpPort, SecureSocketOptions.Auto, cancellationToken);
            await smtp.SendAsync(mime, cancellationToken);
            await smtp.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EmailNotSentException($"Couldn't send email via {settings.SmtpHost}:{settings.SmtpPort}.", exception);
        }
    }
}
