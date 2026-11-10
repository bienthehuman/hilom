using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.Access.Domain;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Access;

[Collection(ApiTestGroup.Name)]
public sealed class PortalRegistrationTests(HilomApiFactory factory)
{
    private const string RegisterUrl = "/api/access/register";
    private const string Password = "Portal-Password-2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"maria.{Guid.NewGuid():N}@family.test";

    private async Task<(HttpClient Client, JsonElement Me)> RegisterAsync(string email)
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();
        var response = await client.PostAsJsonAsync(RegisterUrl, new { displayName = "Maria Dela Cruz", email, password = Password }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (client, await response.ReadJsonAsync());
    }

    private async Task<HttpClient> AdminAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        return admin;
    }

    [Fact]
    public async Task Registering_creates_a_signed_in_portal_account_with_only_the_portal_role()
    {
        var email = NewEmail();

        var (client, registered) = await RegisterAsync(email);

        registered.GetProperty("accountType").GetString().ShouldBe("portal");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        me.GetProperty("email").GetString().ShouldBe(email);
        me.GetProperty("permissions").GetProperty("patients.view").EnumerateArray().Select(s => s.GetString())
            .ShouldBe(["linked"]);

        var id = Guid.Parse(me.GetProperty("id").GetString()!);
        var user = await (await AdminAsync()).GetFromJsonAsync<JsonElement>($"/api/access/users/{id}", Ct);
        user.GetProperty("accountType").GetString().ShouldBe("portal");
        var role = user.GetProperty("roles").EnumerateArray().Single();
        role.GetProperty("isPortal").GetBoolean().ShouldBeTrue();

        var audit = await factory.AuditEntriesAsync(q => q.Where(e => e.UserId == id && e.Event == AuditEvents.Auth.AccountRegistered.Key));
        audit.Single().EntityId.ShouldBe(id);
    }

    [Fact]
    public async Task A_new_portal_account_sees_no_patients_and_no_staff_screens()
    {
        var (client, _) = await RegisterAsync(NewEmail());

        var patients = await client.GetFromJsonAsync<JsonElement>("/api/patients", Ct);

        patients.GetProperty("total").GetInt32().ShouldBe(0);
        (await client.GetAsync("/api/workforce/employees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/access/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_email_can_register_only_once_ignoring_case()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var again = factory.CreateBrowserClient();
        await again.PrimeAntiforgeryAsync();

        var response = await again.PostAsJsonAsync(RegisterUrl, new { displayName = "Someone", email = email.ToUpperInvariant(), password = Password }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("email_taken");
    }

    [Fact]
    public async Task Details_are_checked()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.PostAsJsonAsync(RegisterUrl, new { displayName = " ", email = "not-an-email", password = "short" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("displayName")[0].GetString().ShouldBe("Enter your name.");
        errors.GetProperty("email")[0].GetString().ShouldBe("Enter an email address like name@example.com.");
        errors.GetProperty("password")[0].GetString().ShouldBe("Use at least 12 characters.");
    }

    [Fact]
    public async Task A_portal_account_can_sign_in_again_later()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var later = factory.CreateBrowserClient();

        await later.SignInAsync(email, Password);

        (await later.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("accountType").GetString().ShouldBe("portal");
    }

    [Fact]
    public async Task Portal_accounts_never_get_staff_roles_and_staff_never_get_the_portal_role()
    {
        var admin = await AdminAsync();
        var (portalClient, _) = await RegisterAsync(NewEmail());
        var portalUserId = (await portalClient.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("id").GetString();
        var portalRoleId = await factory.RoleIdAsync("Patient Portal User");
        var nurseRoleId = await factory.RoleIdAsync("Staff Nurse");
        var staff = await factory.CreateUserAsync("Staff-Password-2026", false, "Staff Nurse");

        var staffRoleForPortal = await admin.PutAsJsonAsync($"/api/access/users/{portalUserId}/roles", new { roleIds = new[] { portalRoleId, nurseRoleId } }, Ct);
        var portalRoleForStaff = await admin.PutAsJsonAsync($"/api/access/users/{staff.Id}/roles", new { roleIds = new[] { nurseRoleId, portalRoleId } }, Ct);
        var newStaffWithPortalRole = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email = $"staff.{Guid.NewGuid():N}@hilom.test", displayName = "Staff", password = "Staff-Password-2026", roleIds = new[] { portalRoleId } },
            Ct);

        (await staffRoleForPortal.ProblemCodeAsync()).ShouldBe("portal_account_roles");
        (await portalRoleForStaff.ProblemCodeAsync()).ShouldBe("portal_role_for_portal_accounts");
        (await newStaffWithPortalRole.ProblemCodeAsync()).ShouldBe("portal_role_for_portal_accounts");
        newStaffWithPortalRole.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_portal_role_is_marked_and_cannot_be_deleted()
    {
        var admin = await AdminAsync();
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/access/roles", Ct);
        var portal = roles.EnumerateArray().Single(r => r.GetProperty("isPortal").GetBoolean());

        var delete = await admin.DeleteAsync($"/api/access/roles/{portal.GetProperty("id").GetString()}", Ct);

        portal.GetProperty("name").GetString().ShouldBe("Patient Portal User");
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.ProblemCodeAsync()).ShouldBe("portal_role_protected");
    }

    [Fact]
    public async Task Users_can_be_listed_by_account_type()
    {
        var admin = await AdminAsync();
        var email = NewEmail();
        await RegisterAsync(email);

        async Task<string[]> Emails(string type) =>
            [.. (await admin.GetFromJsonAsync<JsonElement>($"/api/access/users?accountType={type}&q={Uri.EscapeDataString(email)}", Ct))
                .GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()!)];

        (await Emails("portal")).ShouldBe([email]);
        (await Emails("staff")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Existing_accounts_are_staff_accounts()
    {
        var admin = await AdminAsync();

        var me = await admin.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);

        me.GetProperty("accountType").GetString().ShouldBe(nameof(AccountType.Staff).ToLowerInvariant());
    }
}
