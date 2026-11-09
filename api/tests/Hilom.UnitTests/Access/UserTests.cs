using Hilom.Access.Domain;
using Hilom.Infrastructure.Persistence;

namespace Hilom.UnitTests.Access;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly LockoutPolicy Policy = new() { MaxFailedAttempts = 3, Duration = TimeSpan.FromMinutes(15) };

    [Fact]
    public void New_users_are_active_with_a_trimmed_email()
    {
        var user = new User("  nurse@hilom.test ", " Reyes, Ana ");

        user.Email.ShouldBe("nurse@hilom.test");
        user.DisplayName.ShouldBe("Reyes, Ana");
        user.Status.ShouldBe(UserStatus.Active);
        user.SecurityStamp.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Locks_out_after_the_maximum_failed_attempts()
    {
        var user = new User("a@hilom.test", "A");

        user.RecordFailedLogin(Now, Policy);
        user.RecordFailedLogin(Now, Policy);
        user.IsLockedOut(Now).ShouldBeFalse();

        user.RecordFailedLogin(Now, Policy);

        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(15)).ShouldBeFalse();
    }

    [Fact]
    public void A_successful_login_clears_failures()
    {
        var user = new User("a@hilom.test", "A");
        user.RecordFailedLogin(Now, Policy);
        user.RecordFailedLogin(Now, Policy);

        user.RecordSuccessfulLogin(Now);
        user.RecordFailedLogin(Now, Policy);

        user.IsLockedOut(Now).ShouldBeFalse();
        user.LastLoginAt.ShouldBe(Now);
    }

    [Fact]
    public void Changing_the_password_or_disabling_ends_sessions_by_changing_the_stamp()
    {
        var user = new User("a@hilom.test", "A");
        var original = user.SecurityStamp;

        user.ChangePassword("hash");
        var afterPassword = user.SecurityStamp;
        user.Disable();

        afterPassword.ShouldNotBe(original);
        user.SecurityStamp.ShouldNotBe(afterPassword);
        user.Status.ShouldBe(UserStatus.Disabled);
    }

    [Fact]
    public void Upgrading_the_hash_keeps_sessions()
    {
        var user = new User("a@hilom.test", "A");
        var stamp = user.SecurityStamp;

        user.UpgradePasswordHash("stronger-hash");

        user.SecurityStamp.ShouldBe(stamp);
    }

    [Theory]
    [InlineData(UserStatus.Active, "active")]
    [InlineData(UserStatus.Disabled, "disabled")]
    public void Enums_are_stored_as_snake_case_text(UserStatus status, string text)
    {
        EnumText.ToText(status).ShouldBe(text);
        EnumText.FromText<UserStatus>(text).ShouldBe(status);
    }
}
