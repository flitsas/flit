using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13121 (Epic #13090, F1) — resuelve la vigencia de identidad de un mandatario en el tenant donde
/// realmente se registra su validación biométrica: el de la COMPAÑÍA que lo registró (módulo Identidad de
/// la compañía). El módulo Identidad responde 403 a los usuarios de un OT (<c>IdentityModuleAccessFilter</c>),
/// así que ninguna validación nace en el tenant del organismo; buscar allí (comportamiento anterior) hacía
/// que la ficha y el sello del contrato de mandato nunca vieran la identidad.
/// <para>
/// <b>Regla única (AC4).</b> Un mandatario puede estar vinculado a varias compañías
/// (<c>admin.mandate_signer_companies</c>, vínculos activos): se resuelve en cada una y gana el mejor
/// estado (aprobada vigente &gt; en curso &gt; vencida &gt; sin validación). El resultado NO depende del
/// tenant del organismo. Solo si el mandatario no tiene NINGUNA compañía vinculada (registro anterior a
/// la acotación por empresa) se conserva el tenant propio del OT como respaldo, para no cambiar su
/// comportamiento histórico.
/// </para>
/// </summary>
internal static class MandateSignerIdentityTenantResolver
{
    internal readonly record struct SignerRef(
        Guid Id, Guid TransitOfficeId, string DocumentType, string DocumentNumber);

    internal delegate Task<IReadOnlyDictionary<string, IdentityVigenciaResult>> TenantBatchResolver(
        Guid tenantId,
        IReadOnlyCollection<(string DocumentType, string DocumentNumber)> documents,
        CancellationToken cancellationToken);

    public static async Task<Dictionary<Guid, IdentityVigenciaResult>> ResolveAsync(
        FlitDbContext context,
        ITransitOfficeOperationalStatusReader otStatus,
        IReadOnlyCollection<SignerRef> signers,
        TenantBatchResolver resolveForTenant,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IdentityVigenciaResult>();
        if (signers.Count == 0)
        {
            return result;
        }

        var signerIds = signers.Select(s => s.Id).ToList();
        var links = await context.MandateSignerCompanies
            .AsNoTracking()
            .Where(c => signerIds.Contains(c.MandateSignerId) && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tenantsBySigner = links
            .GroupBy(l => l.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.CompanyTenantId).Distinct().ToList());

        // Respaldo: sin compañías vinculadas, el tenant propio del organismo (una consulta por OT).
        var otTenantByOffice = new Dictionary<Guid, Guid?>();
        foreach (var officeId in signers
                     .Where(s => !tenantsBySigner.ContainsKey(s.Id) && s.TransitOfficeId != Guid.Empty)
                     .Select(s => s.TransitOfficeId)
                     .Distinct())
        {
            var status = await otStatus.GetByIdAsync(officeId, cancellationToken).ConfigureAwait(false);
            otTenantByOffice[officeId] = status is { HasTenant: true, TenantId: { } t } ? t : null;
        }

        var porTenant = new Dictionary<Guid, List<SignerRef>>();
        foreach (var s in signers)
        {
            IEnumerable<Guid> tenants = tenantsBySigner.TryGetValue(s.Id, out var own)
                ? own
                : otTenantByOffice.GetValueOrDefault(s.TransitOfficeId) is { } ot ? [ot] : [];
            foreach (var tenant in tenants)
            {
                if (!porTenant.TryGetValue(tenant, out var list))
                {
                    porTenant[tenant] = list = [];
                }

                list.Add(s);
            }
        }

        foreach (var (tenantId, group) in porTenant)
        {
            var documentos = group.Select(s => (s.DocumentType, s.DocumentNumber)).Distinct().ToList();
            var resueltos = await resolveForTenant(tenantId, documentos, cancellationToken).ConfigureAwait(false);

            foreach (var s in group)
            {
                var key = DocumentCanonicalNormalization.IdentidadKey(tenantId, s.DocumentType, s.DocumentNumber);
                var candidato = resueltos.GetValueOrDefault(key, IdentityVigenciaResult.SinValidacion);
                if (!result.TryGetValue(s.Id, out var actual) || Rank(candidato.Status) > Rank(actual.Status))
                {
                    result[s.Id] = candidato;
                }
            }
        }

        return result;
    }

    private static int Rank(string status) => status switch
    {
        IdentityVigenciaEstados.AprobadaVigente => 3,
        IdentityVigenciaEstados.EnCurso => 2,
        IdentityVigenciaEstados.Vencida => 1,
        _ => 0,
    };
}
