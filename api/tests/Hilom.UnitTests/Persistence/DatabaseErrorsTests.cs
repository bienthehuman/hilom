using Hilom.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.UnitTests.Persistence;

public sealed class DatabaseErrorsTests
{
    private static DbUpdateException Failure(string sqlState) =>
        new("save failed", new PostgresException("boom", "ERROR", "ERROR", sqlState, constraintName: "ix_example"));

    [Theory]
    [InlineData(DatabaseErrors.UniqueViolation, 409, "duplicate")]
    [InlineData(DatabaseErrors.ExclusionViolation, 409, "overlap")]
    [InlineData(DatabaseErrors.ForeignKeyViolation, 409, "reference_conflict")]
    [InlineData(DatabaseErrors.CheckViolation, 422, "invalid_value")]
    public void Constraint_errors_map_to_api_errors(string sqlState, int status, string code)
    {
        var violation = DatabaseErrors.Classify(Failure(sqlState)).ShouldNotBeNull();

        violation.StatusCode.ShouldBe(status);
        violation.Code.ShouldBe(code);
        violation.ConstraintName.ShouldBe("ix_example");
    }

    [Fact]
    public void Stale_version_is_a_concurrency_conflict()
    {
        var violation = DatabaseErrors.Classify(new DbUpdateConcurrencyException("stale")).ShouldNotBeNull();

        violation.StatusCode.ShouldBe(409);
        violation.Code.ShouldBe("concurrency_conflict");
    }

    [Fact]
    public void Other_database_errors_are_not_translated()
    {
        DatabaseErrors.Classify(Failure("42P01")).ShouldBeNull();
        DatabaseErrors.Classify(new InvalidOperationException()).ShouldBeNull();
    }
}
