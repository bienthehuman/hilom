using System.Net;
using Hilom.DemoData;
using Hilom.Infrastructure.Seeding;
using Hilom.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Hilom.IntegrationTests.Demo;

/// <summary>
/// Generates a tiny hospital into a real database and checks what the product's own rules would have guaranteed.
/// These tests share one database and one generation, and the ones that reset it leave it as they found it.
/// </summary>
public sealed class DemoDataTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_part_of_the_hospital_is_there()
    {
        var counts = new Dictionary<string, long>
        {
            ["wards"] = await factory.ScalarAsync<long>("SELECT count(*) FROM facility.wards"),
            ["beds"] = await factory.ScalarAsync<long>("SELECT count(*) FROM facility.beds"),
            ["employees"] = await factory.ScalarAsync<long>("SELECT count(*) FROM workforce.employees"),
            ["rosters"] = await factory.ScalarAsync<long>("SELECT count(*) FROM workforce.roster_entries"),
            ["patients"] = await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients"),
            ["allergies"] = await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patient_allergies"),
            ["slots"] = await factory.ScalarAsync<long>("SELECT count(*) FROM scheduling.availability_slots"),
            ["appointments"] = await factory.ScalarAsync<long>("SELECT count(*) FROM scheduling.appointments"),
            ["encounters"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.encounters"),
            ["admissions"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.admissions"),
            ["vitals"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.vital_signs"),
            ["notes"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.clinical_notes"),
            ["orders"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.physician_orders"),
            ["medication administrations"] = await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.medication_administrations"),
            ["audit entries"] = await factory.ScalarAsync<long>("SELECT count(*) FROM access.audit_log"),
        };

        foreach (var (name, count) in counts)
        {
            count.ShouldBeGreaterThan(0, $"no {name}");
        }

        (await factory.ScalarAsync<long>("SELECT count(*) FROM clinical.admissions WHERE discharged_at IS NULL")).ShouldBeGreaterThan(0, "nobody is in a bed");
        foreach (var status in new[] { "requested", "confirmed", "completed", "cancelled" })
        {
            (await factory.ScalarAsync<long>($"SELECT count(*) FROM scheduling.appointments WHERE status = '{status}'"))
                .ShouldBeGreaterThan(0, $"no {status} appointments");
        }

        foreach (var kind in new[] { "event", "data_change", "read" })
        {
            (await factory.ScalarAsync<long>($"SELECT count(*) FROM access.audit_log WHERE kind = '{kind}'")).ShouldBeGreaterThan(0, $"no {kind} audit entries");
        }
    }

    [Fact]
    public async Task Nothing_is_recorded_as_happening_after_the_history_ends()
    {
        var now = $"'{DemoApiFactory.Now:O}'";
        var late = new[]
        {
            $"SELECT count(*) FROM clinical.vital_signs WHERE recorded_at > {now}",
            $"SELECT count(*) FROM clinical.clinical_notes WHERE authored_at > {now}",
            $"SELECT count(*) FROM clinical.medication_administrations WHERE administered_at > {now}",
            $"SELECT count(*) FROM clinical.encounters WHERE started_at > {now} OR ended_at > {now}",
            $"SELECT count(*) FROM clinical.admissions WHERE discharged_at > {now}",
            $"SELECT count(*) FROM patients.patients WHERE created_at > {now}",
            $"SELECT count(*) FROM access.audit_log WHERE occurred_at > {now} AND correlation_id LIKE 'demo-%'",
        };

        foreach (var sql in late)
        {
            (await factory.ScalarAsync<long>(sql)).ShouldBe(0, sql);
        }
    }

    [Fact]
    public async Task Records_on_a_stay_fall_inside_it()
    {
        var outside = await factory.ScalarAsync<long>("""
            SELECT
              (SELECT count(*) FROM clinical.vital_signs v JOIN clinical.encounters e ON e.id = v.encounter_id
                 WHERE v.recorded_at < e.started_at OR v.recorded_at > coalesce(e.ended_at, 'infinity'))
            + (SELECT count(*) FROM clinical.clinical_notes n JOIN clinical.encounters e ON e.id = n.encounter_id
                 WHERE n.authored_at < e.started_at OR n.authored_at > coalesce(e.ended_at, 'infinity'))
            + (SELECT count(*) FROM clinical.medication_administrations m JOIN clinical.encounters e ON e.id = m.encounter_id
                 WHERE m.administered_at < e.started_at OR m.administered_at > coalesce(e.ended_at, 'infinity'))
            """);

        outside.ShouldBe(0);
    }

    [Fact]
    public async Task Nobody_is_given_a_drug_they_are_allergic_to()
    {
        var clashes = await factory.ScalarAsync<long>("""
            SELECT count(*)
            FROM clinical.medication_administrations m
            JOIN clinical.physician_orders o ON o.id = m.order_id
            JOIN patients.patient_allergies a ON a.patient_id = m.patient_id AND a.status = 'active'
            WHERE position(lower(a.allergen) IN lower(o.medication_name)) > 0
               OR position(lower(o.medication_name) IN lower(a.allergen)) > 0
            """);

        clashes.ShouldBe(0);
    }

    [Fact]
    public async Task Doses_follow_a_medication_order_that_was_already_written()
    {
        var wrong = await factory.ScalarAsync<long>("""
            SELECT count(*)
            FROM clinical.medication_administrations m
            JOIN clinical.physician_orders o ON o.id = m.order_id
            WHERE o.order_type <> 'medication' OR m.administered_at < o.ordered_at OR m.encounter_id <> o.encounter_id
            """);

        wrong.ShouldBe(0);
    }

    [Fact]
    public async Task Visits_belong_to_appointments_that_were_checked_in()
    {
        var wrong = await factory.ScalarAsync<long>("""
            SELECT count(*)
            FROM clinical.encounters e
            JOIN scheduling.appointments a ON a.id = e.appointment_id
            WHERE a.status NOT IN ('checked_in', 'completed') OR a.patient_id <> e.patient_id
            """);
        var closedButNotCompleted = await factory.ScalarAsync<long>("""
            SELECT count(*)
            FROM clinical.encounters e
            JOIN scheduling.appointments a ON a.id = e.appointment_id
            WHERE e.status = 'closed' AND a.status <> 'completed'
            """);

        wrong.ShouldBe(0);
        closedButNotCompleted.ShouldBe(0);
    }

    [Fact]
    public async Task Each_current_stay_has_exactly_one_current_bed()
    {
        var wrong = await factory.ScalarAsync<long>("""
            SELECT count(*) FROM (
              SELECT a.encounter_id, count(*) FILTER (WHERE upper_inf(b.during)) AS current_beds
              FROM clinical.admissions a
              LEFT JOIN clinical.bed_assignments b ON b.admission_id = a.encounter_id
              WHERE a.discharged_at IS NULL
              GROUP BY a.encounter_id
              HAVING count(*) FILTER (WHERE upper_inf(b.during)) <> 1) x
            """);
        var dischargedStillInBed = await factory.ScalarAsync<long>("""
            SELECT count(*) FROM clinical.admissions a
            JOIN clinical.bed_assignments b ON b.admission_id = a.encounter_id
            WHERE a.discharged_at IS NOT NULL AND upper_inf(b.during)
            """);

        wrong.ShouldBe(0);
        dischargedStillInBed.ShouldBe(0);
    }

    [Fact]
    public async Task Scripted_family_is_in_place_for_the_portal_demo()
    {
        var stays = await factory.ScalarAsync<string>("""
            SELECT string_agg(w.code, ',')
            FROM patients.patients p
            JOIN clinical.admissions a ON a.patient_id = p.id AND a.discharged_at IS NULL
            JOIN clinical.bed_assignments b ON b.admission_id = a.encounter_id AND upper_inf(b.during)
            JOIN facility.wards w ON w.id = b.ward_id
            WHERE p.first_name = 'Antonio' AND p.last_name = 'Mercado'
            """);
        var link = await factory.ScalarAsync<string>("""
            SELECT l.status || ',' || l.can_book || ',' || l.can_view_records
            FROM patients.patient_account_links l JOIN access.users u ON u.id = l.user_id
            WHERE u.email = 'portal@demo.hilom.test'
            """);

        stays.ShouldBe("W3");
        link.ShouldBe("active,true,true");
    }

    [Fact]
    public async Task The_same_seed_generates_the_same_hospital()
    {
        var before = await factory.FingerprintAsync();

        await factory.ResetAsync();

        (await factory.FingerprintAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_different_seed_generates_a_different_hospital()
    {
        var before = await factory.FingerprintAsync();
        factory.Options.Seed = DemoApiFactory.Seed + 1;
        try
        {
            await factory.ResetAsync();

            (await factory.FingerprintAsync()).ShouldNotBe(before);
        }
        finally
        {
            factory.Options.Seed = DemoApiFactory.Seed;
            await factory.ResetAsync();
        }
    }

    [Fact]
    public async Task Reset_removes_what_visitors_added_and_keeps_the_demo_accounts_working()
    {
        var client = factory.CreateBrowserClient();
        await client.SignInAsync("front.desk@demo.hilom.test", DemoApiFactory.Password);
        var before = await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients");
        await factory.ExecuteAsync("""
            INSERT INTO patients.patients (id, mrn, first_name, last_name, birth_date, sex, address, contact_number,
              emergency_contact_name, emergency_contact_relation, emergency_contact_number, created_at, updated_at)
            VALUES (gen_random_uuid(), 'MRN-VISITOR', 'Visitor', 'Added', '1990-01-01', 'male', 'x', 'x', 'x', 'x', 'x', now(), now())
            """);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients")).ShouldBe(before + 1);

        await factory.ResetAsync();

        (await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients")).ShouldBe(before);
        (await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients WHERE mrn = 'MRN-VISITOR'")).ShouldBe(0);
        var fresh = factory.CreateBrowserClient();
        (await (await ClientWithPrimedAntiforgery(fresh)).LoginAsync("front.desk@demo.hilom.test", DemoApiFactory.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_refuses_a_database_that_is_not_marked_as_demo_data()
    {
        var patients = await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients");
        await factory.ExecuteAsync("ALTER TABLE public.demo_dataset RENAME TO demo_dataset_hidden");
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(factory.ResetAsync);

            (await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients")).ShouldBe(patients);
        }
        finally
        {
            await factory.ExecuteAsync("ALTER TABLE public.demo_dataset_hidden RENAME TO demo_dataset");
        }
    }

    [Fact]
    public async Task Generating_leaves_a_database_that_already_holds_records_alone()
    {
        var patients = await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients");
        await factory.ExecuteAsync("ALTER TABLE public.demo_dataset RENAME TO demo_dataset_hidden");
        try
        {
            await factory.Services.RunSeedersAsync(Ct);

            (await factory.ScalarAsync<long>("SELECT count(*) FROM patients.patients")).ShouldBe(patients);
        }
        finally
        {
            await factory.ExecuteAsync("ALTER TABLE public.demo_dataset_hidden RENAME TO demo_dataset");
        }
    }

    [Fact]
    public async Task While_resetting_the_api_asks_callers_to_come_back()
    {
        var state = factory.Services.GetRequiredService<DemoState>();
        state.Resetting = true;
        try
        {
            var client = factory.CreateBrowserClient();

            var blocked = await client.GetAsync("/api/access/me", Ct);
            var accounts = await client.GetAsync("/api/demo/accounts", Ct);

            blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await blocked.ProblemCodeAsync()).ShouldBe("demo_resetting");
            blocked.Headers.RetryAfter.ShouldNotBeNull();
            accounts.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            state.Resetting = false;
        }

        (await factory.CreateBrowserClient().GetAsync("/api/access/me", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpClient> ClientWithPrimedAntiforgery(HttpClient client)
    {
        await client.PrimeAntiforgeryAsync();
        return client;
    }
}
