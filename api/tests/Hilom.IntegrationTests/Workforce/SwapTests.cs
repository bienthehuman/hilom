using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hilom.IntegrationTests.Infrastructure;
using Hilom.SharedKernel.Auditing;
using static Hilom.IntegrationTests.Workforce.StaffFixture;

namespace Hilom.IntegrationTests.Workforce;

/// <summary>Shift swaps: request → colleague accepts → head nurse approves → the shifts change hands.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SwapTests(HilomApiFactory factory)
{
    private const string Url = "/api/workforce/swaps";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A ward with two nurses (requester, target) and a head nurse, each with a client.</summary>
    private sealed record Setup(
        StaffFixture Staff,
        string Ward,
        string Am,
        string Pm,
        string Requester,
        string Target,
        HttpClient RequesterClient,
        HttpClient TargetClient,
        HttpClient HeadClient);

    private async Task<Setup> SetupAsync()
    {
        var staff = await CreateAsync(factory);
        var ward = await staff.WardAsync();
        var am = await staff.ShiftAsync();
        var pm = await staff.ShiftAsync("14:00", "22:00");
        var requester = await staff.NurseAsync("Requester");
        var target = await staff.NurseAsync("Target");
        var head = await staff.NurseAsync("Head");
        foreach (var nurse in new[] { requester, target, head })
        {
            await staff.AssignAsync(nurse, ward, Today.AddDays(-1));
        }

        return new Setup(
            staff,
            ward,
            am,
            pm,
            requester,
            target,
            await staff.SignInAsEmployeeAsync(requester, "Staff Nurse"),
            await staff.SignInAsEmployeeAsync(target, "Staff Nurse"),
            await staff.SignInAsEmployeeAsync(head, "Head Nurse"));
    }

    private static async Task<string> RosterAsync(Setup s, string employee, string shift, DateOnly day)
    {
        var response = await s.Staff.Admin.PostAsJsonAsync(
            "/api/workforce/roster/entries",
            new { employeeId = employee, wardId = s.Ward, shiftId = shift, dutyDate = Day(day) },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.ReadJsonAsync()).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string mine, string theirs, string? message = "Family event") =>
        client.PostAsJsonAsync(Url, new { requesterEntryId = mine, targetEntryId = theirs, message }, Ct);

    private static async Task<JsonElement> RequestedAsync(HttpClient client, string mine, string theirs)
    {
        var response = await RequestAsync(client, mine, theirs);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> ActAsync(HttpClient client, JsonElement swap, string action, string? note = null) =>
        client.PostAsJsonAsync(
            $"{Url}/{swap.GetProperty("id").GetString()}/{action}",
            new { note, version = swap.GetProperty("version").GetUInt32() },
            Ct);

    private static async Task<JsonElement> ActedAsync(HttpClient client, JsonElement swap, string action, string? note = null)
    {
        var response = await ActAsync(client, swap, action, note);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadJsonAsync();
    }

    /// <summary>Who is on which shift on which day, from the ward grid.</summary>
    private static async Task<string[]> GridAsync(Setup s)
    {
        var grid = await s.Staff.Admin.GetFromJsonAsync<JsonElement>(
            $"/api/workforce/roster/wards/{s.Ward}?from={Day(Today)}&to={Day(Today.AddDays(6))}",
            Ct);
        return
        [
            .. grid.GetProperty("entries").EnumerateArray()
                .Select(e => $"{e.GetProperty("employeeId").GetString()}@{e.GetProperty("dutyDate").GetString()}={e.GetProperty("shift").GetProperty("id").GetString()}")
                .Order(StringComparer.Ordinal),
        ];
    }

    [Fact]
    public async Task An_approved_swap_exchanges_the_two_shifts()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));

        var options = await s.RequesterClient.GetFromJsonAsync<JsonElement>($"{Url}/options?entryId={mine}", Ct);
        options.EnumerateArray().Select(o => o.GetProperty("entryId").GetString()).ShouldContain(theirs);

        var swap = await RequestedAsync(s.RequesterClient, mine, theirs);
        swap.GetProperty("status").GetString().ShouldBe("pending_target");
        swap.GetProperty("youAre").GetString().ShouldBe("requester");

        var inbox = await s.TargetClient.GetFromJsonAsync<JsonElement>($"{Url}/mine", Ct);
        inbox.GetProperty("items")[0].GetProperty("youAre").GetString().ShouldBe("target");

        swap = await ActedAsync(s.TargetClient, swap, "accept", "Happy to");
        swap.GetProperty("status").GetString().ShouldBe("pending_approval");

        var queue = await s.HeadClient.GetFromJsonAsync<JsonElement>($"{Url}/review", Ct);
        queue.GetProperty("items").EnumerateArray().Select(q => q.GetProperty("id").GetString()).ShouldContain(swap.GetProperty("id").GetString());

        swap = await ActedAsync(s.HeadClient, swap, "approve", "OK");
        swap.GetProperty("status").GetString().ShouldBe("approved");
        swap.GetProperty("reviewNote").GetString().ShouldBe("OK");

        (await GridAsync(s)).ShouldBe(
            new[] { $"{s.Target}@{Day(Today.AddDays(1))}={s.Am}", $"{s.Requester}@{Day(Today.AddDays(2))}={s.Pm}" }.Order(StringComparer.Ordinal));

        var mySchedule = await s.RequesterClient.GetFromJsonAsync<JsonElement>($"/api/workforce/roster/mine?from={Day(Today)}&to={Day(Today.AddDays(6))}", Ct);
        mySchedule.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("dutyDate").GetString()).ShouldBe([Day(Today.AddDays(2))]);

        var id = Guid.Parse(swap.GetProperty("id").GetString()!);
        var events = await factory.AuditEntriesAsync(q => q.Where(e => e.EntityId == id && e.Event.StartsWith("workforce.swap.")));
        events.Select(e => e.Event).ShouldBe([AuditEvents.Workforce.SwapApproved.Key, AuditEvents.Workforce.SwapAccepted.Key, AuditEvents.Workforce.SwapRequested.Key]);
    }

    [Fact]
    public async Task Same_day_swaps_work_despite_one_shift_per_day()
    {
        var s = await SetupAsync();
        var day = Today.AddDays(3);
        var mine = await RosterAsync(s, s.Requester, s.Am, day);
        var theirs = await RosterAsync(s, s.Target, s.Pm, day);

        var swap = await RequestedAsync(s.RequesterClient, mine, theirs);
        swap = await ActedAsync(s.TargetClient, swap, "accept");
        await ActedAsync(s.HeadClient, swap, "approve");

        (await GridAsync(s)).ShouldBe(
            new[] { $"{s.Requester}@{Day(day)}={s.Pm}", $"{s.Target}@{Day(day)}={s.Am}" }.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Declined_and_cancelled_swaps_go_no_further()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));

        var declined = await RequestedAsync(s.RequesterClient, mine, theirs);
        declined = await ActedAsync(s.TargetClient, declined, "decline", "Can't that day");
        declined.GetProperty("status").GetString().ShouldBe("declined_by_target");
        declined.GetProperty("targetResponse").GetString().ShouldBe("Can't that day");
        (await (await ActAsync(s.HeadClient, declined, "approve")).ProblemCodeAsync()).ShouldBe("swap_not_waiting");

        // Once closed, the same shifts can be offered again; the requester can then cancel.
        var again = await RequestedAsync(s.RequesterClient, mine, theirs);
        var cancelled = await ActedAsync(s.RequesterClient, again, "cancel");
        cancelled.GetProperty("status").GetString().ShouldBe("cancelled");
        (await (await ActAsync(s.TargetClient, cancelled, "accept")).ProblemCodeAsync()).ShouldBe("swap_not_waiting");
    }

    [Fact]
    public async Task Only_the_right_person_can_act_at_each_step()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));
        var swap = await RequestedAsync(s.RequesterClient, mine, theirs);

        (await ActAsync(s.RequesterClient, swap, "accept")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ActAsync(s.TargetClient, swap, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ActAsync(s.RequesterClient, swap, "approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        swap = await ActedAsync(s.TargetClient, swap, "accept");

        // A head nurse of another ward can't approve it.
        var otherWard = await s.Staff.WardAsync();
        var otherHead = await s.Staff.NurseAsync("OtherHead");
        await s.Staff.AssignAsync(otherHead, otherWard, Today);
        var otherClient = await s.Staff.SignInAsEmployeeAsync(otherHead, "Head Nurse");
        (await ActAsync(otherClient, swap, "approve")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherClient.GetFromJsonAsync<JsonElement>($"{Url}/review", Ct)).GetProperty("items").EnumerateArray()
            .ShouldNotContain(q => q.GetProperty("id").GetString() == swap.GetProperty("id").GetString());

        var denied = await factory.AuditEntriesAsync(q => q.Where(e => e.Event == AuditEvents.Access.Denied.Key && e.EntityId == Guid.Parse(swap.GetProperty("id").GetString()!)));
        denied.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Shifts_in_an_open_swap_cant_be_changed_or_offered_twice()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));
        var other = await RosterAsync(s, s.Target, s.Am, Today.AddDays(4));
        await RequestedAsync(s.RequesterClient, mine, theirs);

        var remove = await s.Staff.Admin.DeleteAsync($"/api/workforce/roster/entries/{theirs}", Ct);
        var offeredAgain = await RequestAsync(s.RequesterClient, mine, other);
        var options = await s.RequesterClient.GetFromJsonAsync<JsonElement>($"{Url}/options?entryId={mine}", Ct);

        (await remove.ProblemCodeAsync()).ShouldBe("entry_in_open_swap");
        (await offeredAgain.ProblemCodeAsync()).ShouldBe("entry_in_open_swap");
        options.EnumerateArray().Select(o => o.GetProperty("entryId").GetString()).ShouldBe([other]);
    }

    [Fact]
    public async Task Approval_rechecks_the_roster_rules()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(5));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));
        var swap = await RequestedAsync(s.RequesterClient, mine, theirs);
        swap = await ActedAsync(s.TargetClient, swap, "accept");

        // The target leaves the ward before the requester's day.
        var assignment = (await s.Staff.Admin.GetFromJsonAsync<JsonElement>($"/api/workforce/ward-assignments?employeeId={s.Target}", Ct)).GetProperty("items")[0];
        (await s.Staff.Admin.PostAsJsonAsync(
            $"/api/workforce/ward-assignments/{assignment.GetProperty("id").GetString()}/end",
            new { lastDay = Day(Today.AddDays(3)), version = assignment.GetProperty("version").GetUInt32() },
            Ct)).EnsureSuccessStatusCode();

        var approve = await ActAsync(s.HeadClient, swap, "approve");

        approve.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await approve.ProblemCodeAsync()).ShouldBe("not_assigned_to_ward");
        (await GridAsync(s)).ShouldContain($"{s.Target}@{Day(Today.AddDays(2))}={s.Pm}");
    }

    [Fact]
    public async Task Swaps_that_would_double_book_someone_are_refused_up_front()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));
        await RosterAsync(s, s.Target, s.Pm, Today.AddDays(1));

        var response = await RequestAsync(s.RequesterClient, mine, theirs);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("already_rostered");
    }

    [Fact]
    public async Task Requests_must_offer_your_own_upcoming_shift_for_a_colleagues_on_the_same_ward()
    {
        var s = await SetupAsync();
        var mine = await RosterAsync(s, s.Requester, s.Am, Today.AddDays(1));
        var theirs = await RosterAsync(s, s.Target, s.Pm, Today.AddDays(2));
        var otherWard = await s.Staff.WardAsync();
        var stranger = await s.Staff.NurseAsync("Stranger");
        await s.Staff.AssignAsync(stranger, otherWard, Today);
        var strangers = (await (await s.Staff.Admin.PostAsJsonAsync(
            "/api/workforce/roster/entries",
            new { employeeId = stranger, wardId = otherWard, shiftId = s.Am, dutyDate = Day(Today.AddDays(2)) },
            Ct)).ReadJsonAsync()).GetProperty("id").GetString()!;

        var notMine = await RequestAsync(s.RequesterClient, theirs, mine);
        var otherWardTarget = await RequestAsync(s.RequesterClient, mine, strangers);
        var (physician, _) = await factory.SignInWithRolesAsync("Physician");

        (await notMine.ProblemCodeAsync()).ShouldBe("swap_not_yours");
        (await otherWardTarget.ProblemCodeAsync()).ShouldBe("swap_target_invalid");
        (await RequestAsync(physician, mine, theirs)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
