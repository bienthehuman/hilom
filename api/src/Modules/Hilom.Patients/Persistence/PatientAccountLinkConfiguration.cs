using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;
using Hilom.Patients.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Patients.Persistence;

internal sealed class PatientAccountLinkConfiguration : IEntityTypeConfiguration<PatientAccountLink>
{
    public const string OneOpenLinkPerPatient = "ux_patient_account_links_user_patient_open";
    public const string OneActiveSelfLink = "ux_patient_account_links_active_self";

    public void Configure(EntityTypeBuilder<PatientAccountLink> builder)
    {
        builder.ToTable("patient_account_links", table =>
        {
            table.HasEnumCheck<PatientAccountLink, LinkRelationship>("relationship");
            table.HasEnumCheck<PatientAccountLink, LinkStatus>("status");

            // Active and revoked links always name the patient; pending and rejected requests may not.
            table.HasCheckConstraint(
                "ck_patient_account_links_patient",
                "status IN ('pending', 'rejected') OR patient_id IS NOT NULL");
            table.HasCheckConstraint(
                "ck_patient_account_links_revoke_reason",
                "status <> 'revoked' OR (revoke_reason IS NOT NULL AND btrim(revoke_reason) <> '')");
        });

        builder.HasOne<User>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_patient_account_links_users_user_id");
        builder.HasOne<Patient>().WithMany().HasForeignKey(l => l.PatientId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_patient_account_links_patients_patient_id");

        builder.Property(l => l.RequestedPatientName).HasMaxLength(PatientAccountLink.NameMaxLength);
        builder.Property(l => l.RequestedMrn).HasMaxLength(PatientAccountLink.MrnMaxLength);
        builder.Property(l => l.Relationship).HasEnumText();
        builder.Property(l => l.RequestNote).HasMaxLength(PatientAccountLink.NoteMaxLength);
        builder.Property(l => l.Status).HasEnumText();
        builder.Property(l => l.ReviewNote).HasMaxLength(PatientAccountLink.NoteMaxLength);
        builder.Property(l => l.RevokeReason).HasMaxLength(PatientAccountLink.NoteMaxLength);

        // The review queue, and "my links" for the portal.
        builder.HasIndex(l => new { l.Status, l.RequestedAt });
        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => l.PatientId, "ByPatient").HasDatabaseName("ix_patient_account_links_patient_id");

        // One open (pending or active) link per account and patient.
        builder.HasIndex(l => new { l.UserId, l.PatientId }, "OpenPerPatient")
            .IsUnique()
            .HasFilter("status IN ('pending', 'active')")
            .HasDatabaseName(OneOpenLinkPerPatient);

        // At most one account is the patient themselves.
        builder.HasIndex(l => l.PatientId, "ActiveSelf")
            .IsUnique()
            .HasFilter("relationship = 'self' AND status = 'active'")
            .HasDatabaseName(OneActiveSelfLink);
    }
}
