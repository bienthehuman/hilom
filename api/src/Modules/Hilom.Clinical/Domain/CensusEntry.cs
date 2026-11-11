namespace Hilom.Clinical.Domain;

/// <summary>
/// One row of <c>clinical.current_census</c> (docs/DATA_MODEL.md, views): a patient in a bed now, with their
/// ward, bed, attending physician and how long they've been admitted. Read-only; the view is built from open
/// bed assignments of stays that haven't been discharged.
/// </summary>
public sealed class CensusEntry
{
    private CensusEntry()
    {
    }

    public Guid BedAssignmentId { get; private set; }

    /// <summary>The inpatient encounter (the admission shares its id).</summary>
    public Guid EncounterId { get; private set; }

    public Guid PatientId { get; private set; }

    public string Mrn { get; private set; } = "";

    public string FirstName { get; private set; } = "";

    public string LastName { get; private set; } = "";

    public Guid WardId { get; private set; }

    public Guid BedId { get; private set; }

    public Guid AttendingPhysicianId { get; private set; }

    public string PhysicianFirstName { get; private set; } = "";

    public string PhysicianLastName { get; private set; } = "";

    public DateTimeOffset AdmittedAt { get; private set; }

    /// <summary>Whole hours since admission, as of the query.</summary>
    public int AdmittedHours { get; private set; }
}
