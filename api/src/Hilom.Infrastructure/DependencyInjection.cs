using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Security;
using Hilom.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hilom.Infrastructure;

public static class DependencyInjection
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<IModule> modules)
    {
        var registry = new ModuleRegistry(modules);
        services.AddSingleton(registry);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.AddScoped<EntityTimestampsInterceptor>();

        // Resolved when a context is first created, not at startup, so tools that only inspect the app
        // (such as build-time OpenAPI generation) run without database settings.
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                DatabaseConnection.Resolve(configuration, environment),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<EntityTimestampsInterceptor>()));

        services.AddOptions<HospitalOptions>()
            .Bind(configuration.GetSection(HospitalOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _),
                "Hospital:TimeZone is not a known time zone.")
            .ValidateOnStart();

        foreach (var module in registry.Modules)
        {
            module.AddServices(services, configuration);
        }

        return services;
    }
}
