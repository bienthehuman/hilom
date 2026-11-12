using FluentValidation;
using Hilom.Infrastructure.Auditing;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Configuration;
using Hilom.Infrastructure.Email;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Security;
using Hilom.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
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
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICurrentPermissions, CurrentPermissions>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddMemoryCache();
        services.AddScoped<EntityTimestampsInterceptor>();
        services.AddScoped<AuditChangesInterceptor>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        services.AddScoped<AuditChain>();
        services.AddOptions<AuditSealingOptions>()
            .Bind(configuration.GetSection(AuditSealingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHostedService<AuditSealingService>();

        // Resolved when a context is first created, not at startup, so tools that only inspect the app
        // (such as build-time OpenAPI generation) run without database settings.
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                DatabaseConnection.Resolve(configuration, environment),
                npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                provider.GetRequiredService<EntityTimestampsInterceptor>(),
                provider.GetRequiredService<AuditChangesInterceptor>()));

        services.AddOptions<HospitalOptions>()
            .Bind(configuration.GetSection(HospitalOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _),
                "Hospital:TimeZone is not a known time zone.")
            .ValidateOnStart();
        services.AddSingleton<HospitalClock>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Configure(options =>
            {
                if (configuration[$"{EmailOptions.SectionName}:{nameof(EmailOptions.SmtpPort)}"] is null
                    && int.TryParse(configuration["HILOM_MAIL_SMTP_PORT"], out var port))
                {
                    options.SmtpPort = port;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        foreach (var module in registry.Modules)
        {
            services.AddValidatorsFromAssembly(module.Assembly, includeInternalTypes: true);
            module.AddServices(services, configuration);
        }

        return services;
    }
}
