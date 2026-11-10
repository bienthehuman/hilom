using Hilom.Patients.Domain;
using Hilom.SharedKernel.Authorization;

namespace Hilom.Patients.Features.Registry;

/// <summary>
/// Which patients a scope reaches (docs/RBAC.md). <c>all</c> reaches everyone. The narrower scopes reach patients
/// through records later slices build: <c>linked</c> through account links (2.4), <c>own</c> through a
/// physician's appointments and encounters (2.6, phase 3), <c>ward</c> through bed assignments (phase 3).
/// Until then they reach no one, so a ward nurse sees no patients yet.
/// </summary>
internal static class PatientScope
{
    public static IQueryable<Patient> WhereInScope(this IQueryable<Patient> patients, AccessScope scope) =>
        scope.IsAll ? patients : patients.Where(_ => false);
}
