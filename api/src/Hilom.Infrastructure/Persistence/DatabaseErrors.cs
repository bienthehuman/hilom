using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hilom.Infrastructure.Persistence;

/// <summary>A database rule the request broke, translated for the API.</summary>
/// <param name="StatusCode">409 for conflicts with existing data, 422 for values the database rejects.</param>
/// <param name="Code">Stable error code returned to clients.</param>
/// <param name="ConstraintName">The PostgreSQL constraint involved, if known.</param>
public sealed record DatabaseRuleViolation(int StatusCode, string Code, string? ConstraintName);

/// <summary>
/// Turns PostgreSQL constraint errors into API errors. The database is the final guard for rules like
/// "no double-booked beds" (see docs/adr/0006-database-enforced-invariants.md).
/// </summary>
public static class DatabaseErrors
{
    public const string UniqueViolation = "23505";
    public const string ExclusionViolation = "23P01";
    public const string ForeignKeyViolation = "23503";
    public const string CheckViolation = "23514";

    public static DatabaseRuleViolation? Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is DbUpdateConcurrencyException)
        {
            return new DatabaseRuleViolation(409, "concurrency_conflict", null);
        }

        if (exception is not DbUpdateException { InnerException: PostgresException postgres })
        {
            return null;
        }

        return postgres.SqlState switch
        {
            UniqueViolation => new DatabaseRuleViolation(409, "duplicate", postgres.ConstraintName),
            ExclusionViolation => new DatabaseRuleViolation(409, "overlap", postgres.ConstraintName),
            ForeignKeyViolation => new DatabaseRuleViolation(409, "reference_conflict", postgres.ConstraintName),
            CheckViolation => new DatabaseRuleViolation(422, "invalid_value", postgres.ConstraintName),
            _ => null,
        };
    }
}
