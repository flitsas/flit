using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// Alcance de compañías cliente del USUARIO que registra o edita un mandatario. Un usuario del OT con alcance
/// <c>especificos</c> solo puede vincular (como propietarias o como asociadas) compañías de SU alcance: cualquier otra se
/// rechaza con <see cref="OtClientScopeExceedsActorException"/> (403, nada se guarda). Sin restricción (Admin OT sin alcance,
/// Super Admin, compañías) no hace nada.
///
/// <para>En la edición solo se valida lo que el actor AGREGA: lo que el mandatario ya tenía fuera de su alcance no lo ve
/// (el listado se lo oculta) y por tanto no viene en su cuerpo; se conserva tal cual en vez de borrarse por omisión.</para>
/// </summary>
internal static class MandateSignerClientScopeGuard
{
    /// <returns><c>null</c> = sin restricción; si no, las compañías permitidas (puede ser vacío: no puede vincular ninguna).</returns>
    public static async Task<HashSet<Guid>?> AllowedAsync(
        IOtUserClientScopeResolver? resolver, Guid transitOfficeId, CancellationToken cancellationToken)
    {
        if (resolver is null)
        {
            return null;
        }

        var restriction = await resolver.GetRestrictionAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        return restriction?.ToHashSet();
    }

    /// <exception cref="OtClientScopeExceedsActorException">Alguna compañía está fuera del alcance.</exception>
    public static void EnsureWithin(HashSet<Guid>? allowed, IEnumerable<Guid> companyTenantIds)
    {
        ArgumentNullException.ThrowIfNull(companyTenantIds);
        if (allowed is not null && companyTenantIds.Any(id => !allowed.Contains(id)))
        {
            throw new OtClientScopeExceedsActorException();
        }
    }

    /// <summary>
    /// Compañías asociadas por organismo a guardar: lo que manda el actor MÁS las que el mandatario ya tenía en ese
    /// organismo y el actor no ve (fuera de su alcance). <c>null</c> (no tocar) se respeta.
    /// </summary>
    public static IReadOnlyList<MandateSignerOfficeCompanies>? KeepHiddenAssociations(
        HashSet<Guid>? allowed,
        IReadOnlyList<MandateSignerOfficeCompanies>? requested,
        IReadOnlyList<MandateSignerOfficeCompanies> persisted)
    {
        if (allowed is null || requested is null)
        {
            return requested;
        }

        return
        [
            .. requested.Select(entry =>
            {
                var hidden = persisted
                    .Where(p => p.TransitOfficeId == entry.TransitOfficeId)
                    .SelectMany(p => p.AssociatedCompanyTenantIds)
                    .Where(id => !allowed.Contains(id));
                return new MandateSignerOfficeCompanies(
                    entry.TransitOfficeId,
                    [.. (entry.AssociatedCompanyTenantIds ?? []).Union(hidden)],
                    entry.AssociatedCompanies);
            }),
        ];
    }

    /// <summary>Asociadas que el actor AGREGA (no estaban ya en ese organismo), a validar contra su alcance.</summary>
    public static IEnumerable<Guid> AddedAssociations(
        IReadOnlyList<MandateSignerOfficeCompanies>? requested,
        IReadOnlyList<MandateSignerOfficeCompanies> persisted) =>
        (requested ?? []).SelectMany(entry =>
        {
            var already = persisted
                .Where(p => p.TransitOfficeId == entry.TransitOfficeId)
                .SelectMany(p => p.AssociatedCompanyTenantIds)
                .ToHashSet();
            return (entry.AssociatedCompanyTenantIds ?? []).Where(id => !already.Contains(id));
        });
}
