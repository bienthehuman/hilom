using Hilom.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hilom.DemoData;

public static class DemoServiceCollectionExtensions
{
    /// <summary>
    /// Binds <c>Demo:*</c>. Only when <c>Demo:Enabled</c> is true does it add the seeder and the nightly reset; otherwise the
    /// only thing present is the "demo accounts" endpoint answering "not enabled".
    /// </summary>
    public static IServiceCollection AddDemoData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => DemoOptions.TryParseResetAt(o.ResetAt, out _), "Demo:ResetAt must be a 24-hour time like 03:00.")
            .ValidateOnStart();
        services.TryAddSingleton<DemoState>();

        if (configuration.GetSection(DemoOptions.SectionName).GetValue<bool>(nameof(DemoOptions.Enabled)))
        {
            services.AddScoped<DemoGenerator>();
            services.AddScoped<IDataSeeder, DemoSeeder>();
            services.AddHostedService<DemoResetService>();
        }

        return services;
    }
}
