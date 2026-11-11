using Hilom.Clinical.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Clinical.Persistence;

/// <summary>Maps the <c>clinical.current_census</c> view. EF doesn't create views: the migration does.</summary>
internal sealed class CensusEntryConfiguration : IEntityTypeConfiguration<CensusEntry>
{
    public const string ViewName = "current_census";

    public void Configure(EntityTypeBuilder<CensusEntry> builder)
    {
        builder.HasNoKey();
        builder.ToView(ViewName, ClinicalModule.SchemaName);
    }
}
