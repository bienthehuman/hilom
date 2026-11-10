using System.ComponentModel.DataAnnotations;

namespace Hilom.Infrastructure.Email;

/// <summary>
/// Outgoing email. The defaults send to the local Mailpit inbox (docker compose), which catches every message
/// instead of delivering it. The live demo keeps a catcher too (docs/CONVENTIONS.md §8).
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string SmtpHost { get; set; } = "localhost";

    /// <summary>Falls back to <c>HILOM_MAIL_SMTP_PORT</c> from <c>.env</c>, then 1025.</summary>
    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 1025;

    [Required]
    [EmailAddress]
    public string FromAddress { get; set; } = "no-reply@hilom.test";

    [Required]
    public string FromName { get; set; } = "Hilom";

    /// <summary>Where the web app is reached, for links in emails (e.g. the invite link).</summary>
    [Required]
    [Url]
    public string AppUrl { get; set; } = "http://localhost:4200";
}
