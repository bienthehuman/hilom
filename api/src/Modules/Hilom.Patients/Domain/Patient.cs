using System.Diagnostics.CodeAnalysis;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;

namespace Hilom.Patients.Domain;

public enum CivilStatus
{
    [SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "A civil status, not a type")]
    Single,
    Married,
    Widowed,
    Separated,
    Annulled,
}

/// <summary>Everything about a patient that the front desk edits in one form (docs/DATA_MODEL.md, patients).</summary>
public sealed record PatientDetails(
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly BirthDate,
    Sex Sex,
    CivilStatus? CivilStatus,
    string? Religion,
    string Address,
    string ContactNumber,
    string EmergencyContactName,
    string EmergencyContactRelation,
    string EmergencyContactNumber);

/// <summary>
/// A person the hospital treats. Identified by a medical record number (MRN) that never changes. Age isn't
/// stored; it's calculated from the date of birth. Who registered the patient, and when, is
/// <see cref="Entity.CreatedBy"/> and <see cref="Entity.CreatedAt"/>.
/// </summary>
public sealed class Patient : Entity, IPatientRecord
{
    public const int NameMaxLength = 100;
    public const int ReligionMaxLength = 60;
    public const int AddressMaxLength = 300;
    public const int ContactMaxLength = 30;
    public const int EmergencyNameMaxLength = 200;
    public const int RelationMaxLength = 50;

    private Patient()
    {
    }

    private Patient(string mrn) => Mrn = mrn;

    /// <summary>Medical record number, e.g. <c>MRN-000123</c>.</summary>
    public string Mrn { get; private set; } = "";

    public string FirstName { get; private set; } = "";

    public string? MiddleName { get; private set; }

    public string LastName { get; private set; } = "";

    public DateOnly BirthDate { get; private set; }

    public Sex Sex { get; private set; }

    public CivilStatus? CivilStatus { get; private set; }

    public string? Religion { get; private set; }

    public string Address { get; private set; } = "";

    public string ContactNumber { get; private set; } = "";

    public string EmergencyContactName { get; private set; } = "";

    public string EmergencyContactRelation { get; private set; } = "";

    public string EmergencyContactNumber { get; private set; } = "";

    /// <summary>Data-change entries for the patient row belong to this patient's access history.</summary>
    Guid IPatientRecord.PatientId => Id;

    public static Patient Register(string mrn, PatientDetails details)
    {
        var patient = new Patient(mrn);
        patient.Update(details);
        return patient;
    }

    /// <summary>Replaces the demographics. Returns the names of the fields that changed (camelCase, as the API names them).</summary>
    public IReadOnlyList<string> Update(PatientDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var changed = new List<string>();

        void Set<T>(T current, T next, Action<T> apply, string field)
        {
            if (!EqualityComparer<T>.Default.Equals(current, next))
            {
                apply(next);
                changed.Add(field);
            }
        }

        Set(FirstName, details.FirstName.Trim(), v => FirstName = v, "firstName");
        Set(MiddleName, Clean(details.MiddleName), v => MiddleName = v, "middleName");
        Set(LastName, details.LastName.Trim(), v => LastName = v, "lastName");
        Set(BirthDate, details.BirthDate, v => BirthDate = v, "birthDate");
        Set(Sex, details.Sex, v => Sex = v, "sex");
        Set(CivilStatus, details.CivilStatus, v => CivilStatus = v, "civilStatus");
        Set(Religion, Clean(details.Religion), v => Religion = v, "religion");
        Set(Address, details.Address.Trim(), v => Address = v, "address");
        Set(ContactNumber, details.ContactNumber.Trim(), v => ContactNumber = v, "contactNumber");
        Set(EmergencyContactName, details.EmergencyContactName.Trim(), v => EmergencyContactName = v, "emergencyContactName");
        Set(EmergencyContactRelation, details.EmergencyContactRelation.Trim(), v => EmergencyContactRelation = v, "emergencyContactRelation");
        Set(EmergencyContactNumber, details.EmergencyContactNumber.Trim(), v => EmergencyContactNumber = v, "emergencyContactNumber");
        return changed;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
