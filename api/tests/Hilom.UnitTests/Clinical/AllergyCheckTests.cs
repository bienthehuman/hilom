using Hilom.Clinical.Domain;

namespace Hilom.UnitTests.Clinical;

public sealed class AllergyCheckTests
{
    [Fact]
    public void An_allergen_in_the_medication_name_matches_ignoring_case()
    {
        AllergyCheck.Matches("Co-Amoxiclav (amoxicillin + clavulanate)", ["Amoxicillin", "Shellfish"]).ShouldBe(["Amoxicillin"]);
        AllergyCheck.Matches("ASPIRIN", ["aspirin"]).ShouldBe(["aspirin"]);
    }

    [Fact]
    public void A_medication_name_inside_a_longer_allergen_matches()
    {
        AllergyCheck.Matches("Ibuprofen", ["Ibuprofen and other NSAIDs"]).ShouldBe(["Ibuprofen and other NSAIDs"]);
    }

    [Fact]
    public void Names_that_dont_overlap_dont_match_even_in_the_same_drug_class()
    {
        // A name match only: penicillin vs amoxicillin is why every allergy is shown too.
        AllergyCheck.Matches("Amoxicillin", ["Penicillin"]).ShouldBeEmpty();
    }

    [Fact]
    public void Blank_names_match_nothing()
    {
        AllergyCheck.Matches(null, ["Penicillin"]).ShouldBeEmpty();
        AllergyCheck.Matches("  ", ["Penicillin"]).ShouldBeEmpty();
        AllergyCheck.Matches("Paracetamol", ["  "]).ShouldBeEmpty();
    }
}
