using System.Globalization;
using System.Net;
using Hilom.Infrastructure.Email;

namespace Hilom.Workforce.Features.Invites;

/// <summary>The invite email. The link carries the token after <c>#</c>, so it never reaches server logs.</summary>
internal static class InviteEmail
{
    public static string AcceptUrl(string appUrl, string token) => $"{appUrl.TrimEnd('/')}/invite#{token}";

    public static EmailMessage Compose(
        string toAddress,
        string firstName,
        string lastName,
        string hospitalName,
        string acceptUrl,
        DateTimeOffset expiresAt,
        TimeZoneInfo timeZone)
    {
        var expires = TimeZoneInfo.ConvertTime(expiresAt, timeZone)
            .ToString("MMMM d, yyyy 'at' HH:mm", CultureInfo.InvariantCulture);
        var subject = $"Set up your {hospitalName} account";

        var text = $"""
            Hello {firstName},

            {hospitalName} has created a Hilom account for you. Choose a password to start using it:

            {acceptUrl}

            This link works once and expires on {expires}.
            If you weren't expecting this email, you can ignore it.
            """;

        var html = $"""
            <p>Hello {Encode(firstName)},</p>
            <p>{Encode(hospitalName)} has created a Hilom account for you. Choose a password to start using it:</p>
            <p><a href="{Encode(acceptUrl)}">Set your password</a></p>
            <p>This link works once and expires on {Encode(expires)}.<br>
            If you weren't expecting this email, you can ignore it.</p>
            """;

        return new EmailMessage(toAddress, $"{firstName} {lastName}", subject, text, html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
