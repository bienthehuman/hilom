using System.Text.RegularExpressions;
using Hilom.Access.Domain;
using Hilom.Access.Seeding;
using Hilom.SharedKernel.Authorization;

namespace Hilom.UnitTests.Access;

/// <summary>
/// docs/RBAC.md is the spec. These tests fail when the code's permission catalog or default roles
/// drift from it, so the two are always updated together.
/// </summary>
public sealed partial class RbacDocumentationTests
{
    private static readonly string Rbac = File.ReadAllText(FindRepoFile("docs/RBAC.md"));

    [Fact]
    public void Code_catalog_matches_the_documented_catalog()
    {
        var section = Rbac[Rbac.IndexOf("## Permission catalog", StringComparison.Ordinal)..Rbac.IndexOf("## Default roles", StringComparison.Ordinal)];
        var documented = CatalogRow().Matches(section)
            .Select(m => (Key: m.Groups["key"].Value, Scopes: m.Groups["scopes"].Value.Replace(" ", "", StringComparison.Ordinal)))
            .ToList();

        var inCode = Perm.All
            .Select(p => (p.Key, Scopes: string.Join(",", p.AllowedScopes.Select(s => s.ToString().ToLowerInvariant()))))
            .ToList();

        inCode.ShouldBe(documented);
    }

    [Fact]
    public void Permission_keys_are_unique_and_prefixed_by_their_module()
    {
        Perm.All.Select(p => p.Key).ShouldBeUnique();
        foreach (var permission in Perm.All)
        {
            permission.Key.ShouldStartWith(permission.Group.ToLowerInvariant() + ".");
            permission.AllowedScopes.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Default_roles_match_the_documented_matrix()
    {
        var table = Rbac[Rbac.IndexOf("| Permission | Sys Admin", StringComparison.Ordinal)..];
        var lines = table.Split("\n\n")[0].Split('\n').Skip(2).Where(l => l.StartsWith('|')).ToList();
        string[] columns = ["HR Officer", "Chief Nurse", "Head Nurse", "Staff Nurse", "Physician", "Front Desk", "Patient Portal User"];

        var documented = columns.ToDictionary(name => name, _ => new List<RoleGrant>());
        foreach (var line in lines)
        {
            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            for (var i = 0; i < columns.Length; i++)
            {
                if (cells[i + 2] is { Length: > 0 } cell)
                {
                    documented[columns[i]].Add(new RoleGrant(cells[0], ScopeFromLetter(cell)));
                }
            }
        }

        DefaultRoles.All.Select(r => r.Name).ShouldBe(columns);
        foreach (var role in DefaultRoles.All)
        {
            role.Grants.ShouldBe(documented[role.Name], $"{role.Name} differs from docs/RBAC.md");
        }
    }

    [Fact]
    public void Every_default_role_is_valid()
    {
        foreach (var role in DefaultRoles.All)
        {
            Role.Create(role.Name, role.Description, role.Grants).IsSuccess.ShouldBeTrue(role.Name);
        }
    }

    private static PermissionScope ScopeFromLetter(string letter) => letter switch
    {
        "A" => PermissionScope.All,
        "W" => PermissionScope.Ward,
        "O" => PermissionScope.Own,
        "L" => PermissionScope.Linked,
        _ => throw new InvalidOperationException($"Unknown scope letter '{letter}' in docs/RBAC.md"),
    };

    private static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Couldn't find {relativePath} above {AppContext.BaseDirectory}");
    }

    [GeneratedRegex(@"^\| `(?<key>[a-z_.]+)` \| (?<scopes>[a-z, ]+) \|", RegexOptions.Multiline)]
    private static partial Regex CatalogRow();
}
