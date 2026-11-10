using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.Access.Seeding;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Authorization;

namespace Hilom.IntegrationTests.Access;

[Collection(ApiTestGroup.Name)]
public sealed class RbacTests(HilomApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> RoleNames =>
        [Role.SystemAdministratorName, .. DefaultRoles.All.Select(r => r.Name)];

    [Fact]
    public async Task System_administrator_and_default_roles_are_seeded()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var roles = await client.GetFromJsonAsync<JsonElement>("/api/access/roles", Ct);

        var names = roles.EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();
        string[] expected = [Role.SystemAdministratorName, .. DefaultRoles.All.Select(r => r.Name)];
        names.ShouldBe(expected, ignoreOrder: true);
        roles[0].GetProperty("isSystem").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_seeded_administrator_sees_every_permission_granted_for_all()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        var permissions = me.GetProperty("permissions");

        var expected = Perm.All.Where(p => p.Allows(PermissionScope.All)).Select(p => p.Key);
        permissions.EnumerateObject().Select(p => p.Name).ShouldBe(expected, ignoreOrder: true);
        permissions.GetProperty("access.roles.manage")[0].GetString().ShouldBe("all");
    }

    /// <summary>The matrix test: each role reaches exactly the endpoints its permissions allow.</summary>
    [Theory]
    [MemberData(nameof(RoleNames))]
    public async Task Each_role_reaches_exactly_what_its_permissions_allow(string roleName)
    {
        var (client, _) = await factory.SignInWithRolesAsync(roleName);
        var target = await factory.CreateUserAsync("Target-Password-1");
        var grants = roleName == Role.SystemAdministratorName
            ? Role.CreateSystemAdministrator().EffectiveGrants()
            : DefaultRoles.All.Single(r => r.Name == roleName).Grants;
        bool Granted(Permission p) => grants.Any(g => g.Permission == p.Key);

        var listRoles = await client.GetAsync("/api/access/roles", Ct);
        var listUsers = await client.GetAsync("/api/access/users", Ct);
        var viewAudit = await client.GetAsync("/api/access/audit", Ct);
        var createRole = await client.PostAsJsonAsync(
            "/api/access/roles",
            new { name = $"Matrix {Guid.NewGuid():N}", description = "", grants = Array.Empty<object>() },
            Ct);
        var setRoles = await client.PutAsJsonAsync(
            $"/api/access/users/{target.Id}/roles",
            new { roleIds = Array.Empty<Guid>() },
            Ct);

        var listEmployees = await client.GetAsync("/api/workforce/employees", Ct);
        var createEmployee = await client.PostAsJsonAsync("/api/workforce/employees", new { }, Ct);
        var listInvites = await client.GetAsync($"/api/workforce/employees/{Guid.CreateVersion7()}/invites", Ct);
        var listWards = await client.GetAsync("/api/facility/wards", Ct);
        var createWard = await client.PostAsJsonAsync("/api/facility/wards", new { }, Ct);
        var createShift = await client.PostAsJsonAsync("/api/workforce/shifts", new { }, Ct);
        var listAssignments = await client.GetAsync("/api/workforce/ward-assignments", Ct);
        var assignWard = await client.PostAsJsonAsync("/api/workforce/ward-assignments", new { }, Ct);

        listRoles.StatusCode.ShouldBe(Granted(Perm.Access.RolesView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        listUsers.StatusCode.ShouldBe(Granted(Perm.Access.UsersView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        viewAudit.StatusCode.ShouldBe(Granted(Perm.Access.AuditView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        createRole.StatusCode.ShouldBe(Granted(Perm.Access.RolesManage) ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        setRoles.StatusCode.ShouldBe(Granted(Perm.Access.UsersManage) ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
        listEmployees.StatusCode.ShouldBe(Granted(Perm.Workforce.EmployeesView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        createEmployee.StatusCode.ShouldBe(Granted(Perm.Workforce.EmployeesManage) ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        listInvites.StatusCode.ShouldBe(Granted(Perm.Workforce.InvitesSend) ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden);
        listWards.StatusCode.ShouldBe(Granted(Perm.Facility.WardsView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        createWard.StatusCode.ShouldBe(Granted(Perm.Facility.WardsManage) ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        createShift.StatusCode.ShouldBe(Granted(Perm.Workforce.ShiftsManage) ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        listAssignments.StatusCode.ShouldBe(Granted(Perm.Workforce.AssignmentsView) ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        assignWard.StatusCode.ShouldBe(Granted(Perm.Workforce.AssignmentsManage) ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_with_no_roles_can_sign_in_but_reach_nothing()
    {
        var (client, _) = await factory.SignInWithRolesAsync();

        var me = await client.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        var roles = await client.GetAsync("/api/access/roles", Ct);

        me.GetProperty("permissions").EnumerateObject().ShouldBeEmpty();
        roles.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Changing_a_roles_permissions_takes_effect_on_the_next_request()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        var created = await admin.PostAsJsonAsync(
            "/api/access/roles",
            new { name = $"Auditors {Guid.NewGuid():N}", description = "", grants = new[] { new { permission = "access.roles.view", scope = "all" } } },
            Ct);
        var role = await created.ReadJsonAsync();
        var roleName = role.GetProperty("name").GetString()!;

        var (member, _) = await factory.SignInWithRolesAsync(roleName);
        (await member.GetAsync("/api/access/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await admin.PutAsJsonAsync(
            $"/api/access/roles/{role.GetProperty("id").GetGuid()}",
            new { name = roleName, description = "", grants = Array.Empty<object>(), version = role.GetProperty("version").GetUInt32() },
            Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await member.GetAsync("/api/access/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Saving_a_role_someone_else_just_changed_is_a_conflict()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        var role = await (await admin.PostAsJsonAsync(
            "/api/access/roles",
            new { name = $"Editors {Guid.NewGuid():N}", description = "", grants = Array.Empty<object>() },
            Ct)).ReadJsonAsync();
        var url = $"/api/access/roles/{role.GetProperty("id").GetGuid()}";
        var staleVersion = role.GetProperty("version").GetUInt32();
        var name = role.GetProperty("name").GetString();

        var first = await admin.PutAsJsonAsync(url, new { name, description = "first", grants = Array.Empty<object>(), version = staleVersion }, Ct);
        var second = await admin.PutAsJsonAsync(url, new { name, description = "second", grants = Array.Empty<object>(), version = staleVersion }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ProblemCodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Grants_outside_the_catalog_are_rejected_per_permission()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var response = await admin.PostAsJsonAsync(
            "/api/access/roles",
            new { name = $"Bad {Guid.NewGuid():N}", description = "", grants = new[] { new { permission = "patients.register", scope = "ward" } } },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.ReadJsonAsync();
        body.GetProperty("code").GetString().ShouldBe("invalid_grants");
        body.GetProperty("errors").GetProperty("patients.register")[0].GetString()!.ShouldContain("all");
    }

    [Fact]
    public async Task The_system_role_cant_be_edited_or_deleted()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        var systemId = await factory.RoleIdAsync(Role.SystemAdministratorName);

        var update = await admin.PutAsJsonAsync(
            $"/api/access/roles/{systemId}",
            new { name = "Root", description = "", grants = Array.Empty<object>(), version = 0u },
            Ct);
        var delete = await admin.DeleteAsync($"/api/access/roles/{systemId}", Ct);

        (await update.ProblemCodeAsync()).ShouldBe("system_role_protected");
        (await delete.ProblemCodeAsync()).ShouldBe("system_role_protected");
    }

    [Fact]
    public async Task Roles_still_held_by_users_cant_be_deleted()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        await factory.CreateUserAsync("Holder-Password-1", disabled: false, "Physician");

        var response = await admin.DeleteAsync($"/api/access/roles/{await factory.RoleIdAsync("Physician")}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("role_in_use");
    }

    [Fact]
    public async Task Nobody_can_change_their_own_roles()
    {
        var (client, user) = await factory.SignInWithRolesAsync(Role.SystemAdministratorName);

        var response = await client.PutAsJsonAsync(
            $"/api/access/users/{user.Id}/roles",
            new { roleIds = Array.Empty<Guid>() },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ProblemCodeAsync()).ShouldBe("cannot_change_own_roles");
    }

    [Fact]
    public async Task The_last_active_system_administrator_keeps_the_role()
    {
        await factory.ResetDatabaseAsync(); // only the seeded administrator holds the role now
        // Someone who may manage users and roles (so may change administrators) but isn't one.
        var (hr, _) = await factory.SignInWithRolesAsync(
            await factory.CreateRoleAsync("access.users.manage", "access.roles.manage"));
        var adminId = (await (await SignInAdminAsync()).GetFromJsonAsync<JsonElement>("/api/access/me", Ct))
            .GetProperty("id").GetGuid();

        var removeLast = await hr.PutAsJsonAsync($"/api/access/users/{adminId}/roles", new { roleIds = Array.Empty<Guid>() }, Ct);

        removeLast.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await removeLast.ProblemCodeAsync()).ShouldBe("last_system_administrator");

        // With a second administrator in place, the first can step down.
        await factory.CreateUserAsync("Second-Admin-1", disabled: false, Role.SystemAdministratorName);
        var stepDown = await hr.PutAsJsonAsync($"/api/access/users/{adminId}/roles", new { roleIds = Array.Empty<Guid>() }, Ct);
        stepDown.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await factory.ResetDatabaseAsync(); // leave the seeded administrator as it was for other tests
    }

    [Fact]
    public async Task Gaining_a_role_takes_effect_on_the_next_request()
    {
        var (member, user) = await factory.SignInWithRolesAsync();
        (await member.GetAsync("/api/access/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await SignInAdminAsync();
        var assign = await admin.PutAsJsonAsync(
            $"/api/access/users/{user.Id}/roles",
            new { roleIds = new[] { await factory.RoleIdAsync("HR Officer") } },
            Ct);

        assign.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await member.GetAsync("/api/access/roles", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpClient> SignInAdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }
}
