namespace Hilom.Clinical.Domain;

/// <summary>
/// Matches a medication's name against the patient's allergens, to point at the allergies that may apply before
/// a dose is recorded. A name match only: it doesn't know drug classes (penicillin vs amoxicillin), so every
/// active allergy is shown as well, and the nurse confirms they checked them.
/// </summary>
public static class AllergyCheck
{
    /// <summary>Allergens whose name is in the medication's name, or the other way round, ignoring case.</summary>
    public static IReadOnlyList<string> Matches(string? medicationName, IEnumerable<string> allergens)
    {
        var medication = medicationName?.Trim();
        if (string.IsNullOrEmpty(medication))
        {
            return [];
        }

        return
        [
            .. allergens
                .Select(a => a.Trim())
                .Where(a => a.Length > 0
                    && (medication.Contains(a, StringComparison.OrdinalIgnoreCase) || a.Contains(medication, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
