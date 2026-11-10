using Hilom.Workforce.Domain;

namespace Hilom.UnitTests.Workforce;

public sealed class ShiftSwapRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static (ShiftSwapRequest Swap, RosterEntry Mine, RosterEntry Theirs) NewSwap()
    {
        var ward = Guid.CreateVersion7();
        var mine = new RosterEntry(Guid.CreateVersion7(), ward, Guid.CreateVersion7(), new DateOnly(2026, 11, 12));
        var theirs = new RosterEntry(Guid.CreateVersion7(), ward, Guid.CreateVersion7(), new DateOnly(2026, 11, 13));
        return (new ShiftSwapRequest(mine, theirs, "  please  "), mine, theirs);
    }

    [Fact]
    public void Goes_from_colleague_to_reviewer()
    {
        var (swap, _, _) = NewSwap();
        swap.Message.ShouldBe("please");

        swap.Accept(Now, null).IsSuccess.ShouldBeTrue();
        swap.Status.ShouldBe(SwapStatus.PendingApproval);
        swap.Approve(Guid.CreateVersion7(), Now, "ok").IsSuccess.ShouldBeTrue();
        swap.Status.ShouldBe(SwapStatus.Approved);
        swap.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void Steps_happen_only_in_order()
    {
        var (swap, _, _) = NewSwap();

        swap.Approve(null, Now, null).Error.ShouldNotBeNull().Code.ShouldBe("swap_not_waiting");
        swap.Decline(Now, "no").IsSuccess.ShouldBeTrue();
        swap.Accept(Now, null).IsSuccess.ShouldBeFalse();
        swap.Cancel().IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Notices_when_a_shift_changed_after_the_request()
    {
        var (swap, mine, theirs) = NewSwap();
        swap.Matches(mine, theirs).ShouldBeTrue();

        theirs.ChangeShift(Guid.CreateVersion7());

        swap.Matches(mine, theirs).ShouldBeFalse();
    }
}
