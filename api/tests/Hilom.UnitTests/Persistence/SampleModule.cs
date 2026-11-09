using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.SharedKernel.Domain;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.UnitTests.Persistence;

/// <summary>A stand-in module with one table, used to check the shared database conventions.</summary>
internal sealed class SampleModule : IModule
{
    public string Name => "Sample";

    public string Schema => "sample";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}

internal sealed class SampleRecord : Entity
{
    public string DisplayName { get; set; } = "";
}

internal sealed class SampleRecordConfiguration : IEntityTypeConfiguration<SampleRecord>
{
    public void Configure(EntityTypeBuilder<SampleRecord> builder) => builder.Property(r => r.DisplayName).HasMaxLength(100);
}

internal static class SampleDb
{
    /// <summary>A context with only the sample module. Never connects; model and change tracking only.</summary>
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseSnakeCaseNamingConvention()
            .EnableServiceProviderCaching(false)
            .Options;

        return new AppDbContext(options, new ModuleRegistry([new SampleModule()]));
    }
}
