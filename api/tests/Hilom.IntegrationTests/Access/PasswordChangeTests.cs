using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;

namespace Hilom.IntegrationTests.Access;

/// <summary>
/// Accounts an administrator creates start with a temporary password, and the API holds them back until the
/// user chooses their own. Anyone can change their own password at any time.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PasswordChangeTests(HilomApiFactory factory)
{
    private const string TemporaryPassword = "Temporary-Pass-2026";
    private const string ChosenPassword = "My-Own-Choice-2026";
    private const string ChangeUrl = "/api/access/me/password";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>An administrator creates a Staff Nurse account with a temporary password.</summary>
    private async Task<(string Email, Guid Id)> CreateNurseAsync()
    {
        var admin = factory.CreateBrowserClient();
        await admin.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        var email = $"temp-{Guid.NewGuid():N}@hilom.test";
        var created = await admin.PostAsJsonAsync(
            "/api/access/users",
            new { email, displayName = "Santos, Mia", password = TemporaryPassword, roleIds = new[] { await factory.RoleIdAsync("Staff Nurse") } },
            Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (email, (await created.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_temporary_password_holds_the_user_back_until_they_choose_their_own()
    {
        var (email, id) = await CreateNurseAsync();
        var nurse = factory.CreateBrowserClient();
        await nurse.SignInAsync(email, TemporaryPassword);

        var me = await nurse.GetFromJsonAsync<JsonElement>("/api/access/me", Ct);
        me.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();

        // A permission endpoint the role grants, and an endpoint any signed-in user may call.
        var census = await nurse.GetAsync("/api/clinical/census", Ct);
        var departments = await nurse.GetAsync("/api/workforce/departments", Ct);
        census.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await census.ProblemCodeAsync()).ShouldBe("password_change_required");
        (await departments.ProblemCodeAsync()).ShouldBe("password_change_required");

        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "access.denied" && e.UserId == id));
        denied.ShouldContain(e => e.Details!.Contains("password_change_required") && e.Details.Contains("/api/clinical/census"));

        var changed = await nurse.PostAsJsonAsync(ChangeUrl, new { currentPassword = TemporaryPassword, newPassword = ChosenPassword }, Ct);

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await changed.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
        (await nurse.GetAsync("/api/clinical/census", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var recorded = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "auth.password.changed" && e.EntityId == id));
        using var details = JsonDocument.Parse(recorded.ShouldHaveSingleItem().Details!);
        details.RootElement.GetProperty("replacedTemporaryPassword").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_the_password_keeps_this_session_ends_the_others_and_retires_the_old_password()
    {
        var (email, _) = await CreateNurseAsync();
        var phone = factory.CreateBrowserClient();
        await phone.SignInAsync(email, TemporaryPassword);
        var wardPc = factory.CreateBrowserClient();
        await wardPc.SignInAsync(email, TemporaryPassword);

        (await wardPc.PostAsJsonAsync(ChangeUrl, new { currentPassword = TemporaryPassword, newPassword = ChosenPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await wardPc.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await phone.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var fresh = factory.CreateBrowserClient();
        await fresh.PrimeAntiforgeryAsync();
        (await (await fresh.LoginAsync(email, TemporaryPassword)).ProblemCodeAsync()).ShouldBe("invalid_credentials");
        var signedIn = await fresh.LoginAsync(email, ChosenPassword);
        (await signedIn.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_wrong_current_password_or_an_unchanged_one_is_refused()
    {
        var (email, id) = await CreateNurseAsync();
        var nurse = factory.CreateBrowserClient();
        await nurse.SignInAsync(email, TemporaryPassword);

        var wrong = await nurse.PostAsJsonAsync(ChangeUrl, new { currentPassword = "Not-The-Password-1", newPassword = ChosenPassword }, Ct);
        var same = await nurse.PostAsJsonAsync(ChangeUrl, new { currentPassword = TemporaryPassword, newPassword = TemporaryPassword }, Ct);
        var shortOne = await nurse.PostAsJsonAsync(ChangeUrl, new { currentPassword = TemporaryPassword, newPassword = "short" }, Ct);

        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var wrongBody = await wrong.ReadJsonAsync();
        wrongBody.GetProperty("code").GetString().ShouldBe("wrong_current_password");
        wrongBody.GetProperty("errors").GetProperty("currentPassword")[0].GetString()
            .ShouldBe("Your current password isn't right.");
        (await same.ReadJsonAsync()).GetProperty("errors").GetProperty("newPassword")[0].GetString()
            .ShouldBe("Choose a password different from your current one.");
        (await shortOne.ReadJsonAsync()).GetProperty("errors").GetProperty("newPassword")[0].GetString()
            .ShouldBe("Use at least 12 characters.");

        var failed = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == "auth.password.changed" && e.EntityId == id));
        failed.ShouldHaveSingleItem().Outcome.ShouldBe(AuditOutcome.Failed);
        (await nurse.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Users_who_chose_their_own_password_are_not_held_back_and_can_change_it_any_time()
    {
        const string password = "Role-Test-Password-1"; // SignInWithRolesAsync's password
        var (client, _) = await factory.SignInWithRolesAsync("Front Desk");

        (await client.GetFromJsonAsync<JsonElement>("/api/access/me", Ct)).GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
        (await client.GetAsync("/api/workforce/departments", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsJsonAsync(ChangeUrl, new { currentPassword = password, newPassword = ChosenPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/workforce/departments", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signed_out_callers_cannot_change_a_password()
    {
        var anonymous = factory.CreateBrowserClient();
        await anonymous.PrimeAntiforgeryAsync();

        (await anonymous.PostAsJsonAsync(ChangeUrl, new { currentPassword = TemporaryPassword, newPassword = ChosenPassword }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
