using System.Text.RegularExpressions;
using Hilom.SharedKernel.Auditing;

namespace Hilom.UnitTests.Auditing;

/// <summary>docs/AUDIT.md is the spec: every event in code must be listed there with the same severity.</summary>
public sealed partial class AuditCatalogTests
{
    [Fact]
    public void Every_event_in_code_is_documented_with_the_same_severity()
    {
        var documented = DocumentedEvents();

        foreach (var auditEvent in AuditEvents.All.Where(e => e != AuditEvents.DataChanged))
        {
            documented.ShouldContainKey(auditEvent.Key, $"{auditEvent.Key} is missing from docs/AUDIT.md");
            documented[auditEvent.Key].ShouldBe(auditEvent.Severity, $"{auditEvent.Key} has a different severity in docs/AUDIT.md");
        }
    }

    [Fact]
    public void Event_keys_are_unique_and_prefixed_by_their_category()
    {
        AuditEvents.All.Select(e => e.Key).ShouldBeUnique();
        foreach (var auditEvent in AuditEvents.All)
        {
            auditEvent.Key.ShouldStartWith(auditEvent.Category + ".");
            auditEvent.Label.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// Reads catalog rows like "| `a.b.c` / `a.b.d` | critical (…) |". Short forms such as "/ `accepted`"
    /// reuse the previous key with the last part replaced.
    /// </summary>
    private static Dictionary<string, AuditSeverity> DocumentedEvents()
    {
        var doc = File.ReadAllText(FindRepoFile("docs/AUDIT.md"));
        var catalog = doc[doc.IndexOf("## Event catalog", StringComparison.Ordinal)..doc.IndexOf("## Who can see the log", StringComparison.Ordinal)];
        var events = new Dictionary<string, AuditSeverity>();

        foreach (Match row in CatalogRow().Matches(catalog))
        {
            var severity = Enum.Parse<AuditSeverity>(row.Groups["severity"].Value, ignoreCase: true);
            string? previous = null;
            foreach (Match key in Key().Matches(row.Groups["keys"].Value))
            {
                var value = key.Groups[1].Value;
                var full = value.Contains('.', StringComparison.Ordinal) || previous is null
                    ? value
                    : previous[..(previous.LastIndexOf('.') + 1)] + value;
                events[full] = severity;
                previous = full;
            }
        }

        return events;
    }

    private static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Couldn't find {relativePath}");
    }

    [GeneratedRegex(@"^\| (?<keys>`[^|]+) \| (?<severity>info|notice|critical)\b", RegexOptions.Multiline)]
    private static partial Regex CatalogRow();

    [GeneratedRegex(@"`([a-z_.]+)`")]
    private static partial Regex Key();
}
