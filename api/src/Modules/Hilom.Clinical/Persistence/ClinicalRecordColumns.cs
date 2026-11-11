using Hilom.Clinical.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

/// <summary>The void columns every <see cref="ClinicalRecord"/> table shares, with the same rules everywhere.</summary>
internal static class ClinicalRecordColumns
{
    /// <summary>A voided entry has when, by whom and a non-blank reason; one that isn't has none of them.</summary>
    public static void HasVoidCheck(this TableBuilder table, string tableName) =>
        table.HasCheckConstraint(
            $"ck_{tableName}_void",
            "(voided_at IS NULL AND voided_by IS NULL AND void_reason IS NULL) OR "
            + "(voided_at IS NOT NULL AND voided_by IS NOT NULL AND length(btrim(void_reason)) > 0)");

    public static void ConfigureVoid<T>(this EntityTypeBuilder<T> builder)
        where T : ClinicalRecord
    {
        builder.Property(r => r.VoidReason).HasMaxLength(ClinicalRecord.VoidReasonMaxLength);
        builder.Ignore(r => r.IsVoided);
        builder.Ignore(r => r.AuthorId);
    }
}
