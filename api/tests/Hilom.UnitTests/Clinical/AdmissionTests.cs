using Hilom.Clinical.Domain;

namespace Hilom.UnitTests.Clinical;

public sealed class AdmissionTests
{
    [Fact]
    public void Admitting_opens_an_inpatient_encounter_its_admission_and_an_open_first_bed()
    {
        var at = new DateTimeOffset(2026, 11, 11, 2, 30, 0, TimeSpan.Zero);
        var parent = Guid.CreateVersion7();
        var admit = new AdmitPatient(Guid.CreateVersion7(), parent, Guid.CreateVersion7(), Guid.CreateVersion7(), " Pneumonia ", " Needs oxygen ");

        var (encounter, admission, bed) = Admission.Admit(admit, Guid.CreateVersion7(), Guid.CreateVersion7(), at);

        encounter.Type.ShouldBe(EncounterType.Inpatient);
        encounter.Status.ShouldBe(EncounterStatus.Open);
        encounter.ParentEncounterId.ShouldBe(parent);
        encounter.ChiefComplaint.ShouldBe("Needs oxygen");
        encounter.StartedAt.ShouldBe(at);
        admission.EncounterId.ShouldBe(encounter.Id);
        admission.PatientId.ShouldBe(admit.PatientId);
        admission.AdmittedAt.ShouldBe(at);
        admission.AdmittingDiagnosis.ShouldBe("Pneumonia");
        admission.DischargedAt.ShouldBeNull();
        bed.AdmissionId.ShouldBe(admission.Id);
        bed.BedId.ShouldBe(admit.BedId);
        bed.Reason.ShouldBe(BedAssignment.AdmissionReason);
        bed.During.LowerBound.ShouldBe(at.UtcDateTime);
        bed.During.LowerBoundIsInclusive.ShouldBeTrue();
        bed.During.UpperBoundInfinite.ShouldBeTrue();
    }

    private static readonly DateTimeOffset At = new(2026, 11, 11, 2, 30, 0, TimeSpan.Zero);

    private static (Encounter Encounter, Admission Admission, BedAssignment Bed) Admitted() =>
        Admission.Admit(
            new AdmitPatient(Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(), "Pneumonia", "Needs oxygen"),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            At);

    [Fact]
    public void A_transfer_closes_the_current_bed_and_the_next_starts_when_it_ends()
    {
        var (_, admission, first) = Admitted();
        var bedId = Guid.CreateVersion7();

        var next = admission.Transfer(first, bedId, Guid.CreateVersion7(), " Needs telemetry ", Guid.CreateVersion7(), At.AddHours(5));

        next.IsSuccess.ShouldBeTrue();
        first.IsCurrent.ShouldBeFalse();
        first.During.UpperBound.ShouldBe(At.AddHours(5).UtcDateTime);
        first.During.UpperBoundIsInclusive.ShouldBeFalse();
        next.Value.AdmissionId.ShouldBe(admission.Id);
        next.Value.BedId.ShouldBe(bedId);
        next.Value.Reason.ShouldBe("Needs telemetry");
        next.Value.During.LowerBound.ShouldBe(first.During.UpperBound);
        next.Value.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void A_transfer_at_the_same_instant_still_leaves_a_non_empty_range()
    {
        var (_, admission, first) = Admitted();

        var next = admission.Transfer(first, Guid.CreateVersion7(), Guid.CreateVersion7(), "Move", Guid.CreateVersion7(), At);

        first.During.UpperBound.ShouldBeGreaterThan(first.During.LowerBound);
        next.Value.During.LowerBound.ShouldBe(first.During.UpperBound);
    }

    [Fact]
    public void A_transfer_to_the_same_bed_is_refused()
    {
        var (_, admission, first) = Admitted();

        var next = admission.Transfer(first, first.BedId, first.WardId, "Move", Guid.CreateVersion7(), At.AddHours(1));

        next.Error!.Code.ShouldBe("same_bed");
        first.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void Discharging_frees_the_bed_closes_the_stay_and_writes_a_discharge_summary_note()
    {
        var (encounter, admission, bed) = Admitted();
        var by = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();

        var note = admission.Discharge(encounter, bed, DischargeDisposition.Home, " Home on oral antibiotics ", by, author, At.AddDays(3));

        note.IsSuccess.ShouldBeTrue();
        bed.IsCurrent.ShouldBeFalse();
        admission.IsDischarged.ShouldBeTrue();
        admission.DischargedAt.ShouldBe(At.AddDays(3));
        admission.DischargeDisposition.ShouldBe(DischargeDisposition.Home);
        admission.DischargedBy.ShouldBe(by);
        encounter.Status.ShouldBe(EncounterStatus.Closed);
        encounter.EndedAt.ShouldBe(At.AddDays(3));
        note.Value.NoteType.ShouldBe(NoteType.DischargeSummary);
        note.Value.EncounterId.ShouldBe(encounter.Id);
        note.Value.Body.ShouldBe("Home on oral antibiotics");
        note.Value.AuthoredBy.ShouldBe(author);

        var again = admission.Transfer(bed, Guid.CreateVersion7(), Guid.CreateVersion7(), "Move", by, At.AddDays(4));
        again.Error!.Code.ShouldBe("patient_already_discharged");
    }
}
