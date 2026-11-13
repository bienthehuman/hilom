using System.Net;
using Hilom.DemoData;
using Hilom.IntegrationTests.Infrastructure;

namespace Hilom.IntegrationTests.Demo;

/// <summary>The demo logins: listed for the sign-in page, one per role, and each lands somewhere that has something to show.</summary>
public sealed class DemoLoginTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sign_in_page_is_offered_one_login_per_role_and_the_shared_password()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/api/demo/accounts", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.ReadJsonAsync();
        body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        body.GetProperty("password").GetString().ShouldBe(DemoApiFactory.Password);
        var accounts = body.GetProperty("accounts").EnumerateArray().ToList();
        accounts.Select(a => a.GetProperty("role").GetString()).ShouldBe(DemoAccountCatalog.Roles.Select(r => r.Role));
        accounts.Where(a => a.GetProperty("portal").GetBoolean()).Select(a => a.GetProperty("role").GetString()).ShouldBe(["Patient Portal User"]);
        accounts.ShouldAllBe(a => a.GetProperty("description").GetString()!.Length > 0);
    }

    [Fact]
    public async Task Every_demo_login_signs_in_with_the_shared_password_as_its_own_role()
    {
        var accounts = (await (await factory.CreateBrowserClient().GetAsync("/api/demo/accounts", Ct)).ReadJsonAsync())
            .GetProperty("accounts").EnumerateArray().ToList();

        foreach (var account in accounts)
        {
            var client = factory.CreateBrowserClient();
            await client.SignInAsync(account.GetProperty("email").GetString()!, DemoApiFactory.Password);

            var email = account.GetProperty("email").GetString()!;
            var me = await (await client.GetAsync("/api/access/me", Ct)).ReadJsonAsync();
            var roles = await factory.ScalarAsync<string>($"""
                SELECT string_agg(r.name, ',') FROM access.users u
                JOIN access.user_roles ur ON ur.user_id = u.id JOIN access.roles r ON r.id = ur.role_id
                WHERE u.email = '{email}'
                """);

            me.GetProperty("email").GetString().ShouldBe(email);
            me.GetProperty("accountType").GetString().ShouldBe(account.GetProperty("portal").GetBoolean() ? "portal" : "staff");
            me.GetProperty("permissions").EnumerateObject().ShouldNotBeEmpty();
            roles.ShouldBe(account.GetProperty("role").GetString(), "one demo login, one role");
        }
    }

    [Fact]
    public async Task Staff_that_are_not_demo_logins_cannot_sign_in_with_the_demo_password()
    {
        var email = await factory.ScalarAsync<string>(
            "SELECT email FROM access.users WHERE email NOT LIKE '%@demo.hilom.test' AND account_type = 'staff' AND email <> 'admin@hilom.test' ORDER BY email LIMIT 1");
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.LoginAsync(email, DemoApiFactory.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_nurse_login_sees_her_own_ward_and_only_that_one()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("nurse@demo.hilom.test", DemoApiFactory.Password);

        var response = await client.GetAsync("/api/clinical/census", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var wards = (await response.ReadJsonAsync()).EnumerateArray().ToList();
        wards.Select(w => w.GetProperty("code").GetString()).ShouldBe(["W3"]);
        wards[0].GetProperty("occupied").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_chief_nurse_login_sees_every_ward()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("chief.nurse@demo.hilom.test", DemoApiFactory.Password);

        var wards = (await (await client.GetAsync("/api/clinical/census", Ct)).ReadJsonAsync()).EnumerateArray().ToList();

        wards.Count.ShouldBe((int)await factory.ScalarAsync<long>("SELECT count(*) FROM facility.wards"));
    }

    [Fact]
    public async Task The_portal_login_reads_the_linked_patients_records_and_nobody_elses()
    {
        var patientId = await factory.ScalarAsync<Guid>("SELECT id FROM patients.patients WHERE first_name = 'Antonio' AND last_name = 'Mercado'");
        var someoneElse = await factory.ScalarAsync<Guid>("SELECT id FROM patients.patients WHERE last_name <> 'Mercado' ORDER BY id LIMIT 1");
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("portal@demo.hilom.test", DemoApiFactory.Password);

        var own = await client.GetAsync($"/api/clinical/portal-records/{patientId}", Ct);
        var other = await client.GetAsync($"/api/clinical/portal-records/{someoneElse}", Ct);

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_administrator_login_reads_the_audit_log()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("admin@demo.hilom.test", DemoApiFactory.Password);

        var response = await client.GetAsync("/api/access/audit?pageSize=5", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadJsonAsync()).GetProperty("total").GetInt32().ShouldBeGreaterThan(100);
    }

    [Fact]
    public async Task The_front_desk_login_is_refused_clinical_notes_like_any_front_desk()
    {
        var encounterId = await factory.ScalarAsync<Guid>("SELECT id FROM clinical.encounters ORDER BY id LIMIT 1");
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("front.desk@demo.hilom.test", DemoApiFactory.Password);

        var response = await client.GetAsync($"/api/clinical/encounters/{encounterId}/notes", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
