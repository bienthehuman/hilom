using System.Globalization;
using System.Threading.RateLimiting;
using Hilom.Access.Authorization;
using Hilom.Access.Domain;
using Hilom.Access.Features.Roles;
using Hilom.Access.Features.Sessions;
using Hilom.Access.Features.Users;
using Hilom.Access.Seeding;
using Hilom.Access.Sessions;
using Hilom.Infrastructure.Authorization;
using Hilom.Infrastructure.Modules;
using Hilom.Infrastructure.Seeding;
using Hilom.Infrastructure.Web;
using Hilom.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hilom.Access;

/// <summary>Users, sign-in sessions, roles, permissions and the audit log.</summary>
public sealed class AccessModule : IModule
{
    public const string LoginRateLimitPolicy = "login";

    public string Name => "Access";

    public string Schema => "access";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IPermissionStore, PermissionStore>();
        services.AddScoped<IDataSeeder, RoleSeeder>();
        services.AddScoped<IDataSeeder, SystemAdministratorSeeder>();

        services.AddAuthentication(Session.Scheme).AddCookie(Session.Scheme);
        services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>, SessionCookieOptions>();

        // Safety net: an endpoint that forgets RequirePermission still needs a signed-in user.
        // The endpoint-coverage test makes sure none forget.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(Session.Scheme).RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryCookie.HeaderName;
            options.Cookie.Name = AntiforgeryCookie.SecretCookie;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, _) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many sign-in attempts",
                        Detail = "Too many sign-in attempts from this network. Wait a minute and try again.",
                        Extensions = { ["code"] = "too_many_attempts" },
                    },
                });
            };
            limiter.AddPolicy(LoginRateLimitPolicy, httpContext =>
            {
                var perMinute = httpContext.RequestServices.GetRequiredService<IOptions<AccessOptions>>()
                    .Value.LoginAttemptsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/antiforgery", SessionEndpoints.GetAntiforgeryToken)
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issue the XSRF-TOKEN cookie");

        endpoints.MapPost("/login", Login.Handle)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithValidation<LoginRequest>()
            .WithName("Login")
            .WithSummary("Sign in with email and password");

        endpoints.MapPost("/logout", SessionEndpoints.Logout)
            .AllowAnonymous()
            .WithName("Logout")
            .WithSummary("End the current session");

        endpoints.MapGet("/me", SessionEndpoints.GetCurrentUser)
            .RequireSignedIn()
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user and their permissions");

        endpoints.MapGet("/permissions", RoleEndpoints.GetPermissionCatalog)
            .RequirePermission(Perm.Access.RolesView)
            .WithName("GetPermissionCatalog")
            .WithSummary("Every permission and the scopes it can be granted with");

        var roles = endpoints.MapGroup("/roles");
        roles.MapGet("", RoleEndpoints.ListRoles)
            .RequirePermission(Perm.Access.RolesView)
            .WithName("ListRoles");
        roles.MapGet("/{id:guid}", RoleEndpoints.GetRole)
            .RequirePermission(Perm.Access.RolesView)
            .WithName("GetRole");
        roles.MapPost("", RoleEndpoints.CreateRole)
            .RequirePermission(Perm.Access.RolesManage)
            .WithValidation<CreateRoleRequest>()
            .WithName("CreateRole");
        roles.MapPut("/{id:guid}", RoleEndpoints.UpdateRole)
            .RequirePermission(Perm.Access.RolesManage)
            .WithValidation<UpdateRoleRequest>()
            .WithName("UpdateRole");
        roles.MapDelete("/{id:guid}", RoleEndpoints.DeleteRole)
            .RequirePermission(Perm.Access.RolesManage)
            .WithName("DeleteRole");

        var users = endpoints.MapGroup("/users");
        users.MapGet("", UserEndpoints.ListUsers)
            .RequirePermission(Perm.Access.UsersView)
            .WithName("ListUsers")
            .WithSummary("Users, paged, with search and status filter");
        users.MapGet("/{id:guid}", UserEndpoints.GetUser)
            .RequirePermission(Perm.Access.UsersView)
            .WithName("GetUser");
        users.MapPost("", UserEndpoints.CreateUser)
            .RequirePermission(Perm.Access.UsersManage)
            .WithValidation<CreateUserRequest>()
            .WithName("CreateUser")
            .WithSummary("Create a user with a temporary password and roles");
        users.MapPost("/{id:guid}/disable", UserEndpoints.DisableUser)
            .RequirePermission(Perm.Access.UsersManage)
            .WithName("DisableUser")
            .WithSummary("Block sign-in and end the user's sessions");
        users.MapPost("/{id:guid}/enable", UserEndpoints.EnableUser)
            .RequirePermission(Perm.Access.UsersManage)
            .WithName("EnableUser");
        users.MapPost("/{id:guid}/unlock", UserEndpoints.UnlockUser)
            .RequirePermission(Perm.Access.UsersManage)
            .WithName("UnlockUser")
            .WithSummary("Clear a lockout after too many wrong passwords");

        endpoints.MapPut("/users/{userId:guid}/roles", SetUserRoles.Handle)
            .RequirePermission(Perm.Access.UsersManage)
            .WithValidation<SetUserRolesRequest>()
            .WithName("SetUserRoles")
            .WithSummary("Replace a user's roles");
    }
}
