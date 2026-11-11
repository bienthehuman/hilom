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
}
