using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hilom.Api;
using Hilom.Api.Configuration;
using Hilom.Api.ErrorHandling;
using Hilom.Api.Security;
using Hilom.Infrastructure;
using Hilom.Infrastructure.Persistence;
using Hilom.Infrastructure.Seeding;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Same HILOM_DB_* values Docker Compose uses, so the API finds the database on the right port.
    builder.Configuration.AddDotEnvFile();
}

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment, HilomModules.All);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Hilom API";
    document.Info.Description = "Hilom at Aruga: healing and care, in one hospital system.";
    document.Servers = [];
    return Task.CompletedTask;
}));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<AntiforgeryValidation>();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();

var api = app.MapGroup("/api");
foreach (var module in HilomModules.All)
{
    module.MapEndpoints(api.MapGroup(module.RoutePrefix).WithTags(module.Name));
}

if (app.Environment.IsDevelopment())
{
    await app.MigrateDatabaseAsync();
    await app.Services.RunSeedersAsync();
}

await app.RunAsync();

/// <summary>Entry point. Public so integration tests can host the app.</summary>
public partial class Program;
