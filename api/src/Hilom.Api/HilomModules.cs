using Hilom.Access;
using Hilom.Clinical;
using Hilom.Facility;
using Hilom.Infrastructure.Modules;
using Hilom.Patients;
using Hilom.Scheduling;
using Hilom.Workforce;

namespace Hilom.Api;

/// <summary>Every module the app runs, in dependency order (see docs/ARCHITECTURE.md §3).</summary>
internal static class HilomModules
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new AccessModule(),
        new FacilityModule(),
        new WorkforceModule(),
        new PatientsModule(),
        new SchedulingModule(),
        new ClinicalModule(),
    ];
}
