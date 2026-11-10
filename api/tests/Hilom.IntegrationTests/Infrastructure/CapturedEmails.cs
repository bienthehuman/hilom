using System.Collections.Concurrent;
using Hilom.Infrastructure.Email;

namespace Hilom.IntegrationTests.Infrastructure;

/// <summary>Stands in for the SMTP sender: keeps every message so tests can read links from them.</summary>
public sealed class CapturedEmails : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    private readonly ConcurrentDictionary<string, bool> _failFor = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    /// <summary>Makes sending to this address fail, as if the mail server were down.</summary>
    public void FailFor(string address) => _failFor[address] = true;

    public EmailMessage LastTo(string address) => _sent.Last(m => string.Equals(m.ToAddress, address, StringComparison.OrdinalIgnoreCase));

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_failFor.ContainsKey(message.ToAddress))
        {
            throw new EmailNotSentException("Simulated mail server failure.", new IOException("Connection refused"));
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
