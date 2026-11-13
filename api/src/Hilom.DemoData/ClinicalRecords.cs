using System.Globalization;
using Hilom.Clinical.Domain;
using Hilom.Infrastructure.Auditing;
using Hilom.SharedKernel.Auditing;

namespace Hilom.DemoData;

/// <summary>
/// Writes the records that hang off an encounter (vitals, notes, orders, medication administrations) the way the
/// endpoints do: the domain type, then the audit event it records. The caller saves, as the person who did them.
/// </summary>
internal sealed class ClinicalRecords(SeedSession s)
{
    public VitalSigns Vitals(Encounter encounter, PatientInfo patient, Person nurse, DateTimeOffset at, bool acute, bool full)
    {
        s.Act(nurse.UserId, at);
        var measurements = Measure(patient, acute, full);
        var vitals = new VitalSigns(encounter.Id, patient.Id, nurse.EmployeeId, at, measurements, null);
        s.Db.Add(vitals);
        s.Audit.Event(AuditEvents.Clinical.VitalsRecorded, new AuditOptions
        {
            Target = new AuditTarget(nameof(VitalSigns), vitals.Id),
            PatientId = patient.Id,
            Details = new { encounterId = encounter.Id, measured = measurements.Taken() },
        });
        return vitals;
    }

    public ClinicalNote Note(Encounter encounter, PatientInfo patient, NoteType type, string body, Person author, DateTimeOffset at)
    {
        s.Act(author.UserId, at);
        var note = new ClinicalNote(encounter.Id, patient.Id, type, body, author.EmployeeId, at);
        s.Db.Add(note);
        s.Audit.Event(AuditEvents.Clinical.NoteWritten, new AuditOptions
        {
            Target = new AuditTarget(nameof(ClinicalNote), note.Id),
            PatientId = patient.Id,
            Details = new { encounterId = encounter.Id, type },
        });
        return note;
    }

    /// <summary>A medication order for something the patient isn't allergic to, as the allergy warning would insist.</summary>
    public (PhysicianOrder Order, int[] Hours, bool AsNeeded)? MedicationOrder(Encounter encounter, PatientInfo patient, Person physician, DateTimeOffset at, ICollection<string> alreadyOrdered)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var med = s.Random.Pick(DemoCatalog.Medications);
            if (alreadyOrdered.Contains(med.Name) || AllergyCheck.Matches(med.Name, patient.Allergens).Count > 0)
            {
                continue;
            }

            s.Act(physician.UserId, at);
            var details = new MedicationDetails(med.Name, med.Dose, med.Route, med.Frequency);
            var order = new PhysicianOrder(
                encounter.Id, patient.Id, physician.EmployeeId, at, OrderType.Medication,
                string.Create(CultureInfo.InvariantCulture, $"{med.Name} {med.Dose} {med.Route}, {med.Frequency}."), details);
            s.Db.Add(order);
            alreadyOrdered.Add(med.Name);
            s.Audit.Event(AuditEvents.Clinical.OrderCreated, new AuditOptions
            {
                Target = new AuditTarget(nameof(PhysicianOrder), order.Id),
                PatientId = patient.Id,
                Details = new { encounterId = encounter.Id, orderType = order.OrderType, medicationName = order.MedicationName },
            });
            return (order, med.Hours, med.Frequency.Contains("as needed", StringComparison.Ordinal));
        }

        return null;
    }

    public PhysicianOrder OtherOrder(Encounter encounter, PatientInfo patient, Person physician, DateTimeOffset at)
    {
        s.Act(physician.UserId, at);
        var type = s.Random.Pick((OrderType.Laboratory, 0.35), (OrderType.Nursing, 0.3), (OrderType.Diet, 0.15), (OrderType.Other, 0.2));
        var order = new PhysicianOrder(encounter.Id, patient.Id, physician.EmployeeId, at, type, s.Random.Pick(DemoCatalog.OtherOrders), null);
        s.Db.Add(order);
        s.Audit.Event(AuditEvents.Clinical.OrderCreated, new AuditOptions
        {
            Target = new AuditTarget(nameof(PhysicianOrder), order.Id),
            PatientId = patient.Id,
            Details = new { encounterId = encounter.Id, orderType = order.OrderType },
        });
        return order;
    }

    public MedicationAdministration Dose(PhysicianOrder order, PatientInfo patient, Person nurse, DateTimeOffset at)
    {
        s.Act(nurse.UserId, at);
        var outcome = s.Random.Pick(
            (MedicationOutcome.Given, 0.9), (MedicationOutcome.Held, 0.04), (MedicationOutcome.Refused, 0.03), (MedicationOutcome.NotAvailable, 0.03));
        var notes = outcome == MedicationOutcome.Given ? null : s.Random.Pick(DemoCatalog.NotGivenReasons);
        var dose = new MedicationAdministration(order, nurse.EmployeeId, at, outcome, order.Dose, notes);
        s.Db.Add(dose);
        s.Audit.Event(AuditEvents.Clinical.MedicationAdministered, new AuditOptions
        {
            Target = new AuditTarget(nameof(MedicationAdministration), dose.Id),
            PatientId = patient.Id,
            Details = new
            {
                encounterId = order.EncounterId,
                orderId = order.Id,
                medicationName = order.MedicationName,
                outcome,
                allergiesChecked = patient.Allergens.Count > 0,
                allergyMatches = AllergyCheck.Matches(order.MedicationName, patient.Allergens),
            },
        });
        return dose;
    }

    /// <summary>A person opened the record. One read per person, patient and sitting, as the access history shows it.</summary>
    public void Viewed(Person who, PatientInfo patient, DateTimeOffset at)
    {
        s.Act(who.UserId, at);
        s.Audit.Read(AuditEvents.Patients.RecordViewed, new AuditOptions
        {
            Target = new AuditTarget("Patient", patient.Id),
            PatientId = patient.Id,
        });
    }

    private VitalMeasurements Measure(PatientInfo patient, bool acute, bool full)
    {
        var feverish = acute && s.Random.Chance(0.25);
        var temperature = feverish ? 37.6m + s.Random.Next(0, 17) / 10m : 36.2m + s.Random.Next(0, 11) / 10m;
        var systolic = Math.Clamp(patient.Systolic + s.Random.Next(-9, 10) + (acute ? s.Random.Next(0, 15) : 0), 85, 210);
        var diastolic = Math.Clamp(60 + (systolic - 100) / 3 + s.Random.Next(-6, 7), 45, 115);
        var oxygen = acute && s.Random.Chance(0.08) ? s.Random.Next(88, 94) : Math.Min(100, 96 + s.Random.Next(-1, 5));
        var pain = acute && s.Random.Chance(0.15) ? s.Random.Next(5, 9) : s.Random.Next(0, 4);
        return new VitalMeasurements(
            temperature,
            (short)systolic,
            (short)diastolic,
            (short)(62 + s.Random.Next(0, 28) + (feverish ? 12 : 0)),
            (short)(14 + s.Random.Next(0, 7)),
            (short)oxygen,
            (short)pain,
            full ? patient.WeightKg : null,
            full ? patient.HeightCm : null);
    }
}
