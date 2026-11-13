using System.Reflection;
using Hilom.Access;
using Hilom.Api;
using Hilom.Clinical;
using Hilom.Facility;
using Hilom.Infrastructure.Modules;
using Hilom.Patients;
using Hilom.Scheduling;
using Hilom.SharedKernel.Domain;
using Hilom.Workforce;
using NetArchTest.Rules;

namespace Hilom.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private const string Api = "Hilom.Api";
    private const string Infrastructure = "Hilom.Infrastructure";
    private const string DemoData = "Hilom.DemoData";

    private static readonly Dictionary<string, Assembly> Modules = new()
    {
        ["Hilom.Access"] = typeof(AccessModule).Assembly,
        ["Hilom.Facility"] = typeof(FacilityModule).Assembly,
        ["Hilom.Workforce"] = typeof(WorkforceModule).Assembly,
        ["Hilom.Patients"] = typeof(PatientsModule).Assembly,
        ["Hilom.Scheduling"] = typeof(SchedulingModule).Assembly,
        ["Hilom.Clinical"] = typeof(ClinicalModule).Assembly,
    };

    /// <summary>
    /// Which modules each module may use (docs/ARCHITECTURE.md §3, including indirect dependencies).
    /// Access and Facility are at the bottom; Clinical is at the top.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedDependencies = new()
    {
        ["Hilom.Access"] = [],
        ["Hilom.Facility"] = [],
        ["Hilom.Workforce"] = ["Hilom.Access", "Hilom.Facility"],
        ["Hilom.Patients"] = ["Hilom.Access"],
        ["Hilom.Scheduling"] = ["Hilom.Workforce", "Hilom.Patients", "Hilom.Access", "Hilom.Facility"],
        ["Hilom.Clinical"] = ["Hilom.Patients", "Hilom.Workforce", "Hilom.Facility", "Hilom.Scheduling", "Hilom.Access"],
    };

    public static TheoryData<string> ModuleNames => [.. Modules.Keys];

    [Fact]
    public void Shared_kernel_depends_on_no_other_hilom_project()
    {
        var forbidden = Modules.Keys.Append(Api).Append(Infrastructure).Append(DemoData).ToArray();

        AssertNoDependency(Types.InAssembly(typeof(Entity).Assembly), forbidden);
    }

    [Fact]
    public void Infrastructure_never_depends_on_modules_or_the_host()
    {
        var forbidden = Modules.Keys.Append(Api).Append(DemoData).ToArray();

        AssertNoDependency(Types.InAssembly(typeof(IModule).Assembly), forbidden);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Modules_only_depend_on_modules_below_them(string module)
    {
        var forbidden = Modules.Keys
            .Where(other => other != module && !AllowedDependencies[module].Contains(other))
            .Append(Api)
            .Append(DemoData)
            .ToArray();

        AssertNoDependency(Types.InAssembly(Modules[module]), forbidden);
    }

    [Fact]
    public void The_dependency_map_covers_every_module()
    {
        AllowedDependencies.Keys.ShouldBe(Modules.Keys, ignoreOrder: true);
    }

    [Fact]
    public void Every_module_is_registered_in_the_host_exactly_once()
    {
        var registered = HilomModules.All.Select(m => m.GetType().Assembly).ToList();

        registered.ShouldBe(Modules.Values, ignoreOrder: true);
        registered.Distinct().Count().ShouldBe(registered.Count);
    }

    [Fact]
    public void Module_schemas_and_routes_are_unique_lowercase_names()
    {
        foreach (var module in HilomModules.All)
        {
            module.Schema.ShouldMatch("^[a-z][a-z_]*$");
            module.RoutePrefix.ShouldMatch("^[a-z][a-z-]*$");
        }

        HilomModules.All.Select(m => m.Schema).ShouldBeUnique();
        HilomModules.All.Select(m => m.RoutePrefix).ShouldBeUnique();
    }

    private static void AssertNoDependency(Types types, string[] forbiddenNamespaces)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(forbiddenNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"These types break a module boundary: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
