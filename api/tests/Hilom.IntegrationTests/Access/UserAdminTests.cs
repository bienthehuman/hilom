using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests.Access;

[Collection(ApiTestGroup.Name)]
public sealed class UserAdminTests(HilomApiFactory factory)
{
    private const string Password = "Temporary-Pass-2026";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    [Fact]
    public async Task Users_can_be_searched_filtered_and_paged()
    {
        var admin = await AdminAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie" })
        {
            var created = await admin.PostAsJsonAsync(
                "/api/access/users",
                new { email = $"{name.ToLowerInvariant()}-{marker}@hilom.test", displayName = $"{name} {marker}", password = Password },
                Ct);
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var page = await admin.GetFromJsonAsync<JsonElement>($"/api/access/users?q={marker}&pageSize=2&sort=-name", Ct);

        page.GetProperty("total").GetInt32().ShouldBe(3);
        page.GetProperty("pageSize").GetInt32().ShouldBe(2);
        page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("displayName").GetString())
            .ShouldBe([$"Charlie {marker}", $"Bravo {marker}"]);
    }

    [Fact]
    public async Task A_created_user_can_sign_in_with_the_temporary_password_and_holds_their_roles()
    {
        var admin = await AdminAsync();
        var email = $"new-{Guid.NewGuid():N}@hilom.test";

        var created = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email, displayName = "Reyes, Ana", password = Password, roleIds = new[] { await factory.RoleIdAsync("Staff Nurse") } },
            Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await created.ReadJsonAsync();
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("roles")[0].GetProperty("name").GetString().ShouldBe("Staff Nurse");

        var nurse = factory.CreateBrowserClient();
        await nurse.SignInAsync(email, Password);
        var me = await nurse.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        me.GetProperty("permissions").GetProperty("clinical.vitals.record")[0].GetString().ShouldBe("ward");
    }

    [Fact]
    public async Task Duplicate_emails_and_short_passwords_are_rejected()
    {
        var admin = await AdminAsync();

        var duplicate = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email = HilomApiFactory.AdminEmail.ToUpperInvariant(), displayName = "Copy", password = Password },
            Ct);
        var weak = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email = $"weak-{Guid.NewGuid():N}@hilom.test", displayName = "Weak", password = "short" },
            Ct);

        (await duplicate.ProblemCodeAsync()).ShouldBe("email_taken");
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.ReadJsonAsync()).GetProperty("errors").GetProperty("password")[0].GetString()
            .ShouldBe("Use at least 12 characters.");
    }

    [Fact]
    public async Task Disabling_ends_the_session_and_blocks_sign_in_until_enabled()
    {
        var admin = await AdminAsync();
        var user = await factory.CreateUserAsync(Password);
        var session = factory.CreateBrowserClient();
        await session.SignInAsync(user.Email, Password);

        (await admin.PostAsync($"/api/access/users/{user.Id}/disable", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await session.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var blocked = factory.CreateBrowserClient();
        await blocked.PrimeAntiforgeryAsync();
        (await (await blocked.LoginAsync(user.Email, Password)).ProblemCodeAsync()).ShouldBe("invalid_credentials");

        (await admin.PostAsync($"/api/access/users/{user.Id}/enable", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await blocked.LoginAsync(user.Email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unlocking_lets_a_locked_out_user_sign_in_again()
    {
        var admin = await AdminAsync();
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();
        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(user.Email, "Wrong-Password-1");
        }

        var listed = await admin.GetFromJsonAsync<JsonElement>($"/api/access/users/{user.Id}", Ct);
        listed.GetProperty("isLockedOut").GetBoolean().ShouldBeTrue();

        (await admin.PostAsync($"/api/access/users/{user.Id}/unlock", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.LoginAsync(user.Email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_can_disable_their_own_account()
    {
        var (client, user) = await factory.SignInWithRolesAsync(Role.SystemAdministratorName);

        var response = await client.PostAsync($"/api/access/users/{user.Id}/disable", null, Ct);

        (await response.ProblemCodeAsync()).ShouldBe("cannot_disable_self");
    }

    [Fact]
    public async Task HR_can_assign_ordinary_roles_but_not_make_or_unmake_administrators()
    {
        var (hr, _) = await factory.SignInWithRolesAsync("HR Officer");
        var colleague = await factory.CreateUserAsync(Password);
        var administrator = await factory.CreateUserAsync(Password, disabled: false, Role.SystemAdministratorName);

        var ordinary = await hr.PutAsJsonAsync(
            $"/api/access/users/{colleague.Id}/roles",
            new { roleIds = new[] { await factory.RoleIdAsync("Physician") } },
            Ct);
        var promote = await hr.PutAsJsonAsync(
            $"/api/access/users/{colleague.Id}/roles",
            new { roleIds = new[] { await factory.RoleIdAsync(Role.SystemAdministratorName) } },
            Ct);
        var disableAdmin = await hr.PostAsync($"/api/access/users/{administrator.Id}/disable", null, Ct);
        var createAdmin = await hr.PostAsJsonAsync(
            "/api/access/users",
            new
            {
                email = $"sneaky-{Guid.NewGuid():N}@hilom.test",
                displayName = "Sneaky",
                password = Password,
                roleIds = new[] { await factory.RoleIdAsync(Role.SystemAdministratorName) },
            },
            Ct);

        ordinary.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await promote.ProblemCodeAsync()).ShouldBe("system_administrator_requires_roles_manage");
        (await disableAdmin.ProblemCodeAsync()).ShouldBe("system_administrator_requires_roles_manage");
        (await createAdmin.ProblemCodeAsync()).ShouldBe("system_administrator_requires_roles_manage");
    }

    [Fact]
    public async Task The_last_active_administrator_cant_be_disabled()
    {
        await factory.ResetDatabaseAsync(); // only the seeded administrator holds the role now
        var (otherAdmin, _) = await factory.SignInWithRolesAsync(Role.SystemAdministratorName);
        var admin = await AdminAsync();
        var seededId = (await admin.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("id").GetGuid();
        var otherId = (await otherAdmin.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("id").GetGuid();

        // Two administrators: one may disable the other.
        (await otherAdmin.PostAsync($"/api/access/users/{seededId}/disable", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // otherAdmin is now the last active administrator. Someone who may manage users and roles,
        // but isn't an administrator, tries to disable them.
        var managerRole = await factory.CreateRoleAsync("access.users.manage", "access.roles.manage");
        var (manager, _) = await factory.SignInWithRolesAsync(managerRole);
        var lastOne = await manager.PostAsync($"/api/access/users/{otherId}/disable", null, Ct);

        (await lastOne.ProblemCodeAsync()).ShouldBe("last_system_administrator");

        await factory.ResetDatabaseAsync(); // restore the seeded administrator for other tests
    }
}
