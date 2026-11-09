using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hilom.Infrastructure.Persistence;

/// <summary>
/// Stores enums as snake_case text (e.g. <c>on_leave</c>) with a CHECK constraint, matching the JSON the API returns.
/// See docs/DATA_MODEL.md: text + CHECK instead of PostgreSQL enum types, because they are easier to migrate.
/// </summary>
public static class EnumText
{
    public static string ToText<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static TEnum FromText<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().First(value => ToText(value) == text);

    /// <summary>Maps the property to text using the snake_case names.</summary>
    public static PropertyBuilder<TEnum> HasEnumText<TEnum>(this PropertyBuilder<TEnum> property)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(property);
        return property
            .HasConversion(value => ToText(value), text => FromText<TEnum>(text))
            .HasMaxLength(32);
    }

    /// <summary>Adds <c>CHECK (column IN (...))</c> listing every value of the enum.</summary>
    public static TableBuilder<TEntity> HasEnumCheck<TEntity, TEnum>(this TableBuilder<TEntity> table, string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(table);
        table.HasCheckConstraint($"ck_{table.Name}_{column}", CheckSql<TEnum>(column));
        return table;
    }

    /// <summary>Same as above, for tables of owned types (e.g. a role's grants).</summary>
    public static OwnedNavigationTableBuilder<TOwner, TDependent> HasEnumCheck<TOwner, TDependent, TEnum>(
        this OwnedNavigationTableBuilder<TOwner, TDependent> table,
        string column)
        where TOwner : class
        where TDependent : class
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(table);
        table.HasCheckConstraint($"ck_{table.Name}_{column}", CheckSql<TEnum>(column));
        return table;
    }

    private static string CheckSql<TEnum>(string column)
        where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetValues<TEnum>().Select(value => $"'{ToText(value)}'"))})";
}
