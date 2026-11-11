using System.Linq.Expressions;
using Hilom.SharedKernel.Auditing;
using Hilom.SharedKernel.Domain;
using Hilom.SharedKernel.Results;

namespace Hilom.Patients.Domain;

public enum AllergyCategory
{
    Drug,
    Food,
    Environmental,
    Other,
}

public enum AllergySeverity
{
    Mild,
    Moderate,
    Severe,
}

public enum AllergyStatus
{
    /// <summary>Current. Shown to everyone treating the patient; severe ones in the patient header.</summary>
    Active,

    /// <summary>No longer applies (e.g. outgrown). Kept for history.</summary>
    Resolved,

    /// <summary>Recorded by mistake. Kept, struck through, with the reason.</summary>
    EnteredInError,
}

/// <summary>What the clinician fills in. Status changes have their own actions.</summary>
public sealed record AllergyDetails(string Allergen, AllergyCategory Category, AllergySeverity Severity, string? Reaction);

/// <summary>
/// A patient's allergy (docs/DATA_MODEL.md, patients). Never deleted: a mistake is marked
/// <see cref="AllergyStatus.EnteredInError"/> with a reason. At most one active entry per allergen.
/// </summary>
public sealed class PatientAllergy : Entity, IPatientRecord
{
    public const int AllergenMaxLength = 100;
    public const int ReactionMaxLength = 300;
    public const int NoteMaxLength = 500;

    /// <summary>
    /// The allergies that need everyone's attention: active and severe. The patient header banner and the
    /// ward board show these. Usable in queries.
    /// </summary>
    public static readonly Expression<Func<PatientAllergy, bool>> IsActiveAndSevere =
        a => a.Status == AllergyStatus.Active && a.Severity == AllergySeverity.Severe;

    private PatientAllergy()
    {
    }

    public PatientAllergy(Guid patientId, AllergyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        PatientId = patientId;
        Apply(details);
    }

    public Guid PatientId { get; private set; }

    /// <summary>What the patient reacts to, e.g. <c>Penicillin</c>. Unique among active entries, ignoring case.</summary>
    public string Allergen { get; private set; } = "";

    public AllergyCategory Category { get; private set; }

    public AllergySeverity Severity { get; private set; }

    /// <summary>What happens, e.g. "hives" or "anaphylaxis". Empty when unknown.</summary>
    public string? Reaction { get; private set; }

    public AllergyStatus Status { get; private set; } = AllergyStatus.Active;

    /// <summary>Why it was resolved or entered in error. Required for entered in error.</summary>
    public string? StatusNote { get; private set; }

    /// <summary>Changes the details. Returns the names of the fields that changed. Only active entries can be edited.</summary>
    public Result<IReadOnlyList<string>> Update(AllergyDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status != AllergyStatus.Active)
        {
            return Result.Failure<IReadOnlyList<string>>(PatientErrors.AllergyNotActive);
        }

        var before = (Allergen, Category, Severity, Reaction);
        Apply(details);
        var changed = new List<string>();
        if (before.Allergen != Allergen)
        {
            changed.Add("allergen");
        }

        if (before.Category != Category)
        {
            changed.Add("category");
        }

        if (before.Severity != Severity)
        {
            changed.Add("severity");
        }

        if (before.Reaction != Reaction)
        {
            changed.Add("reaction");
        }

        return Result.Success<IReadOnlyList<string>>(changed);
    }

    /// <summary>Marks an active entry resolved or entered in error.</summary>
    public Result ChangeStatus(AllergyStatus status, string? note)
    {
        if (Status != AllergyStatus.Active)
        {
            return Result.Failure(PatientErrors.AllergyNotActive);
        }

        var cleaned = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (status == AllergyStatus.EnteredInError && cleaned is null)
        {
            return Result.Failure(PatientErrors.InvalidAllergy("note", "Say what was wrong with this entry."));
        }

        if (status == AllergyStatus.Active)
        {
            return Result.Failure(PatientErrors.InvalidAllergy("status", "Choose resolved or entered in error."));
        }

        Status = status;
        StatusNote = cleaned;
        return Result.Success();
    }

    private void Apply(AllergyDetails details)
    {
        Allergen = details.Allergen.Trim();
        Category = details.Category;
        Severity = details.Severity;
        Reaction = string.IsNullOrWhiteSpace(details.Reaction) ? null : details.Reaction.Trim();
    }
}
