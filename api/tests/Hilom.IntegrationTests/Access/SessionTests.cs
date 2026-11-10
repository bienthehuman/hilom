using System.Net;
using System.Net.Http.Json;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Net.Http.Headers;

namespace Hilom.IntegrationTests.Access;

[Collection(ApiTestGroup.Name)]
public sealed class SessionTests(HilomApiFactory factory)
{
    private const string Password = "Correct-Horse-2026";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeded_administrator_can_sign_in_and_see_themselves()
    {
        var client = factory.CreateBrowserClient();

        await client.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);
        var me = await client.GetAsync("/api/access/me", Ct);

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await me.ReadJsonAsync();
        body.GetProperty("email").GetString().ShouldBe(HilomApiFactory.AdminEmail);
        body.GetProperty("displayName").GetString().ShouldBe("System Administrator");
    }

    [Fact]
    public async Task Email_is_matched_without_regard_to_case()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.LoginAsync(user.Email.ToUpperInvariant(), Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Session_cookie_cannot_be_read_by_scripts_or_sent_cross_site()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.LoginAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var cookies = SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList());
        var session = cookies.Single(c => c.Name == "hilom.session");
        session.HttpOnly.ShouldBeTrue();
        session.SameSite.ShouldBe(SameSiteMode.Strict);
    }

    [Fact]
    public async Task Without_a_session_the_current_user_is_unauthorized()
    {
        var response = await factory.CreateBrowserClient().GetAsync("/api/access/me", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var wrongPassword = await client.LoginAsync(user.Email, "Not-The-Password-1");
        var unknownEmail = await client.LoginAsync("nobody@hilom.test", "Not-The-Password-1");

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongPassword.ProblemCodeAsync()).ShouldBe("invalid_credentials");
        (await unknownEmail.ProblemCodeAsync()).ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Disabled_account_gets_the_same_answer_as_a_wrong_password()
    {
        var user = await factory.CreateUserAsync(Password, disabled: true);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.LoginAsync(user.Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ProblemCodeAsync()).ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            (await (await client.LoginAsync(user.Email, "Wrong-Password-1")).ProblemCodeAsync())
                .ShouldBe("invalid_credentials");
        }

        var fifth = await client.LoginAsync(user.Email, "Wrong-Password-1");
        var correct = await client.LoginAsync(user.Email, Password);

        (await fifth.ProblemCodeAsync()).ShouldBe("account_locked");
        var locked = await correct.ReadJsonAsync();
        locked.GetProperty("code").GetString().ShouldBe("account_locked");
        locked.GetProperty("retryAfterMinutes").GetInt32().ShouldBe(15);
    }

    [Fact]
    public async Task Sign_in_without_the_antiforgery_token_is_rejected()
    {
        var client = factory.CreateBrowserClient();

        var response = await client.LoginAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("invalid_antiforgery_token");
    }

    [Fact]
    public async Task Missing_fields_return_messages_for_each_field()
    {
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        var response = await client.PostAsJsonAsync("/api/access/login", new { email = "", password = "" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");
        errors.GetProperty("email")[0].GetString().ShouldBe("Enter your email.");
        errors.GetProperty("password")[0].GetString().ShouldBe("Enter your password.");
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var logout = await client.PostAsync("/api/access/logout", null, Ct);
        var me = await client.GetAsync("/api/access/me", Ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabling_a_user_ends_their_existing_session()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(user.Email, Password);
        (await client.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await factory.UpdateUserAsync(user.Id, u => u.Disable());

        (await client.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changing_the_password_ends_existing_sessions()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(user.Email, Password);

        await factory.UpdateUserAsync(user.Id, u => u.ChangePassword("new-hash-from-elsewhere"));

        (await client.GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_users_get_404_for_unknown_api_routes()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync(HilomApiFactory.AdminEmail, HilomApiFactory.AdminPassword);

        var response = await client.GetAsync("/api/does-not-exist", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Too_many_sign_in_attempts_from_one_address_are_slowed_down()
    {
        await using var strictFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Access:LoginAttemptsPerMinute", "2"));
        var client = strictFactory.CreateDefaultClient(new AntiforgeryHandler(), new CookieContainerHandler());
        await client.PrimeAntiforgeryAsync();

        await client.LoginAsync("nobody@hilom.test", "Wrong-Password-1");
        await client.LoginAsync("nobody@hilom.test", "Wrong-Password-1");
        var third = await client.LoginAsync("nobody@hilom.test", "Wrong-Password-1");

        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        third.Headers.RetryAfter.ShouldNotBeNull();
        (await third.ProblemCodeAsync()).ShouldBe("too_many_attempts");
    }

    [Fact]
    public async Task Several_sign_ins_to_one_account_at_the_same_moment_all_succeed()
    {
        var user = await factory.CreateUserAsync(Password);
        var clients = Enumerable.Range(0, 6).Select(_ => factory.CreateBrowserClient()).ToList();
        await Task.WhenAll(clients.Select(c => c.PrimeAntiforgeryAsync()));

        var responses = await Task.WhenAll(clients.Select(c => c.LoginAsync(user.Email, Password)));

        responses.Select(r => r.StatusCode).ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_count_of_wrong_passwords()
    {
        var user = await factory.CreateUserAsync(Password);
        var client = factory.CreateBrowserClient();
        await client.PrimeAntiforgeryAsync();

        for (var i = 0; i < 4; i++)
        {
            await client.LoginAsync(user.Email, "Wrong-Password-1");
        }

        (await client.LoginAsync(user.Email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var i = 0; i < 4; i++)
        {
            (await (await client.LoginAsync(user.Email, "Wrong-Password-1")).ProblemCodeAsync()).ShouldBe("invalid_credentials");
        }
    }
}
