using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hilom.Access.Domain;
using Hilom.Infrastructure.Email;

namespace Hilom.Access.Features.EmailVerification;

/// <summary>
/// Six-digit codes that prove a portal user reads their email. Only a hash is stored; a code works for
/// <see cref="Lifetime"/>, for <see cref="MaxAttempts"/> wrong tries, and a new one can be sent after
/// <see cref="ResendAfter"/>.
/// </summary>
internal static class EmailCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;

    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Tied to the user, so one account's hash says nothing about another's code.</summary>
    public static string Hash(Guid userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")));

    public static bool Matches(User user, string code) =>
        user.EmailCodeHash is { } stored
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored),
            Encoding.ASCII.GetBytes(Hash(user.Id, code)));

    /// <summary>Stores a new code on the user (saved by the caller) and returns the email that carries it.</summary>
    public static EmailMessage Issue(User user, DateTimeOffset now, string hospitalName)
    {
        var code = NewCode();
        user.IssueEmailCode(Hash(user.Id, code), now, now + Lifetime);
        return Compose(user.Email, user.DisplayName, code, hospitalName);
    }

    public static EmailMessage Compose(string toAddress, string displayName, string code, string hospitalName)
    {
        var minutes = (int)Lifetime.TotalMinutes;
        var subject = $"Your Hilom code: {code}";

        var text = $"""
            Hello {displayName},

            Enter this code in the {hospitalName} patient portal to confirm your email:

            {code}

            It works for {minutes} minutes. If you didn't create an account, you can ignore this email.
            """;

        var html = $"""
            <p>Hello {Encode(displayName)},</p>
            <p>Enter this code in the {Encode(hospitalName)} patient portal to confirm your email:</p>
            <p style="font-size:24px;font-weight:600;letter-spacing:4px">{code}</p>
            <p>It works for {minutes} minutes. If you didn't create an account, you can ignore this email.</p>
            """;

        return new EmailMessage(toAddress, displayName, subject, text, html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
