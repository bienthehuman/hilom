using Hilom.Access.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.UnitTests.Access;

public sealed class RoleTests
{
    [Fact]
    public void A_role_accepts_grants_with_allowed_scopes()
    {
        var result = Role.Create("Ward clerk", "Front desk for one ward", [new(Perm.Patients.View.Key, PermissionScope.Ward)]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Grants.ShouldHaveSingleItem().Scope.ShouldBe(PermissionScope.Ward);
    }

    [Fact]
    public void A_scope_the_catalog_doesnt_allow_is_rejected_with_a_reason()
    {
        // Registering patients is only ever granted for all patients.
        var result = Role.Create("Odd", "", [new(Perm.Patients.Register.Key, PermissionScope.Ward)]);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("invalid_grants");
        result.Error.FieldErrors![Perm.Patients.Register.Key][0].ShouldContain("only be granted with: all");
    }

    [Fact]
    public void Unknown_and_duplicate_permissions_are_rejected()
    {
        var result = Role.Create(
            "Odd",
            "",
            [
                new("billing.invoices.view", PermissionScope.All),
                new(Perm.Patients.View.Key, PermissionScope.All),
                new(Perm.Patients.View.Key, PermissionScope.Ward),
            ]);

        result.Error!.FieldErrors!.Keys.ShouldBe(["patients.view", "billing.invoices.view"], ignoreOrder: true);
    }

    [Fact]
    public void The_system_role_cant_be_edited()
    {
        var admin = Role.CreateSystemAdministrator();

        admin.SetGrants([]).Error.ShouldBe(AccessErrors.SystemRoleProtected);
        admin.Rename("Root", "").Error.ShouldBe(AccessErrors.SystemRoleProtected);
    }

    [Fact]
    public void The_system_role_holds_every_permission_that_can_be_granted_for_all()
    {
        var grants = Role.CreateSystemAdministrator().EffectiveGrants();

        grants.ShouldAllBe(g => g.Scope == PermissionScope.All);
        grants.Select(g => g.Permission).ShouldContain(Perm.Access.RolesManage.Key);
        grants.Select(g => g.Permission).ShouldContain(Perm.Clinical.VitalsRecord.Key);

        // Personal actions that only exist at "own" scope aren't something an administrator does for themselves.
        grants.Select(g => g.Permission).ShouldNotContain(Perm.Workforce.SwapsRequest.Key);
        grants.Select(g => g.Permission).ShouldNotContain(Perm.Patients.LinksRequest.Key);
    }
}

public sealed class PermissionSetTests
{
    private static PermissionSet Of(params (Permission Permission, PermissionScope[] Scopes)[] grants) =>
        new(grants.ToDictionary(g => g.Permission.Key, g => (IReadOnlySet<PermissionScope>)g.Scopes.ToHashSet()));

    [Fact]
    public void Scopes_from_several_roles_combine()
    {
        var set = Of((Perm.Patients.View, [PermissionScope.Linked, PermissionScope.Ward]));

        set.ScopesFor(Perm.Patients.View).ShouldBe([PermissionScope.Ward, PermissionScope.Linked]);
        set.Has(Perm.Patients.View).ShouldBeTrue();
    }

    [Fact]
    public void All_makes_narrower_scopes_irrelevant()
    {
        var set = Of((Perm.Patients.View, [PermissionScope.Own, PermissionScope.All]));

        set.ScopesFor(Perm.Patients.View).ShouldBe([PermissionScope.All]);
        set.HasAll(Perm.Patients.View).ShouldBeTrue();
    }

    [Fact]
    public void Missing_permissions_have_no_scopes()
    {
        PermissionSet.Empty.Has(Perm.Access.UsersView).ShouldBeFalse();
        PermissionSet.Empty.ScopesFor(Perm.Access.UsersView).ShouldBeEmpty();
    }
}
