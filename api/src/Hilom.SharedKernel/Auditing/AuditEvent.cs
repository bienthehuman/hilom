namespace Hilom.SharedKernel.Auditing;

public enum AuditKind
{
    /// <summary>A named business or security event, e.g. a sign-in or a transfer.</summary>
    Event,

    /// <summary>Field-level before/after values, written automatically for every saved change.</summary>
    DataChange,

    /// <summary>Someone opened a sensitive record (e.g. a patient chart).</summary>
    Read,
}

public enum AuditSeverity
{
    Info,
    Notice,
    Critical,
}

public enum AuditOutcome
{
    Success,
    Denied,
    Failed,
}

/// <summary>An entry in the audit event catalog (docs/AUDIT.md). Severity is fixed per event.</summary>
/// <param name="Key">Stable identifier, e.g. <c>access.role.permissions_changed</c>. Never rename once released.</param>
/// <param name="Label">What happened, in plain words, for the audit viewer.</param>
public sealed record AuditEvent(string Key, string Category, AuditSeverity Severity, string Label)
{
    public override string ToString() => Key;
}

/// <summary>
/// Marks a property whose value must never reach the audit log (password hashes, tokens, note bodies).
/// Data-change entries still record that it changed, with the value replaced by <c>[redacted]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute;

/// <summary>Implemented by records that belong to one patient, so their audit entries can be found per patient.</summary>
public interface IPatientRecord
{
    Guid PatientId { get; }
}
