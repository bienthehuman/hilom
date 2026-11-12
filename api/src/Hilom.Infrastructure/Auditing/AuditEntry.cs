using System.Net;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Infrastructure.Auditing;

/// <summary>
/// One row of <c>access.audit_log</c> (docs/DATA_MODEL.md, docs/AUDIT.md). Insert-only: a database trigger rejects
/// updates and deletes, except filling the tamper-evidence columns once (<see cref="AuditChain"/>).
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    internal AuditEntry(AuditEvent auditEvent, AuditKind kind, AuditOutcome outcome, DateTimeOffset occurredAt, AuditRequestInfo request)
    {
        Id = Guid.CreateVersion7();
        OccurredAt = occurredAt;
        Kind = kind;
        Event = auditEvent.Key;
        Category = auditEvent.Category;
        Severity = auditEvent.Severity;
        Outcome = outcome;
        UserId = request.UserId;
        CorrelationId = request.CorrelationId;
        IpAddress = request.IpAddress;
        UserAgent = request.UserAgent;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditKind Kind { get; private set; }

    public string Event { get; private set; } = "";

    public string Category { get; private set; } = "";

    public AuditSeverity Severity { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    /// <summary>Who acted. Empty for failed sign-ins with an unknown email and for system actions.</summary>
    public Guid? UserId { get; internal set; }

    /// <summary>The email typed on a failed sign-in.</summary>
    public string? AttemptedIdentifier { get; internal set; }

    /// <summary>The patient this concerns, so a patient's access history can be listed.</summary>
    public Guid? PatientId { get; internal set; }

    /// <summary>Set when a portal user acted through a patient account link.</summary>
    public Guid? ActingForPatientLinkId { get; internal set; }

    public string? EntityType { get; internal set; }

    public Guid? EntityId { get; internal set; }

    /// <summary>JSON <c>{ "field": [old, new] }</c> for data changes.</summary>
    public string? Changes { get; internal set; }

    /// <summary>JSON with event-specific facts, e.g. before/after role lists.</summary>
    public string? Details { get; internal set; }

    /// <summary>The request's trace id. Groups a named event with the data changes it caused.</summary>
    public string CorrelationId { get; private set; } = "";

    public IPAddress? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    // Tamper evidence, filled once by the sealing job (AuditChain).
    public byte[]? PrevHash { get; private set; }

    public byte[]? Hash { get; private set; }

    public DateTimeOffset? SealedAt { get; private set; }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public const string Schema = "access";
    public const string Table = "audit_log";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(Table, Schema, table =>
        {
            table.HasEnumCheck<AuditEntry, AuditKind>("kind");
            table.HasEnumCheck<AuditEntry, AuditSeverity>("severity");
            table.HasEnumCheck<AuditEntry, AuditOutcome>("outcome");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasEnumText();
        builder.Property(e => e.Severity).HasEnumText();
        builder.Property(e => e.Outcome).HasEnumText();
        builder.Property(e => e.Event).HasMaxLength(100);
        builder.Property(e => e.Category).HasMaxLength(40);
        builder.Property(e => e.AttemptedIdentifier).HasColumnType("citext").HasMaxLength(320);
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.Changes).HasColumnType("jsonb");
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.Property(e => e.CorrelationId).HasMaxLength(100);
        builder.Property(e => e.UserAgent).HasMaxLength(500);

        builder.HasIndex(e => e.OccurredAt, "ByTime").HasDatabaseName("ix_audit_log_occurred_at");
        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        builder.HasIndex(e => new { e.PatientId, e.OccurredAt });
        builder.HasIndex(e => new { e.Event, e.OccurredAt });
        builder.HasIndex(e => e.CorrelationId);
        builder.HasIndex(e => e.OccurredAt, "CriticalByTime")
            .HasDatabaseName("ix_audit_log_critical")
            .HasFilter("severity = 'critical'");

        // The hash chain (AuditChain): its order, and the entries still waiting to be sealed.
        builder.HasIndex(e => new { e.SealedAt, e.OccurredAt, e.Id }, "ChainOrder")
            .HasDatabaseName("ix_audit_log_chain_order")
            .HasFilter("sealed_at IS NOT NULL");
        builder.HasIndex(e => new { e.OccurredAt, e.Id }, "Unsealed")
            .HasDatabaseName("ix_audit_log_unsealed")
            .HasFilter("sealed_at IS NULL");
    }
}
