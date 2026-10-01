using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13176 — reporte y retiro de las asociaciones mandatario ↔ empresa por Representante Legal
/// (<c>admin.mandate_signer_represented_companies</c>, DDL 54). Lo consume SOLO el Super Admin (lo impone el
/// endpoint), por eso lee cross-tenant con RLS apagado solo dentro de la transacción. Las fuentes se materializan
/// por separado y se combinan en memoria (mismo patrón que <see cref="DbMandateSignerReader"/>).
/// <para>La proyección no incluye documento ni ruta de firma del mandatario: solo su nombre y los NIT de las
/// empresas asociadas, que es lo que el reporte debe comunicar al cliente.</para>
/// </summary>
internal sealed class RepresentedAssociationRetirementStore : IRepresentedAssociationRetirementStore
{
    private readonly FlitDbContext _context;

    public RepresentedAssociationRetirementStore(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<RepresentedAssociationImpactRow>> ListImpactedAsync(
        CancellationToken cancellationToken = default) =>
        CrossTenantRead.ExecuteAsync<IReadOnlyList<RepresentedAssociationImpactRow>>(
            _context, () => LoadAsync(cancellationToken), cancellationToken);

    public Task<int> RetireActiveAsync(CancellationToken cancellationToken = default) =>
        CrossTenantRead.ExecuteAsync(
            _context,
            async () =>
            {
                var rows = await _context.MandateSignerRepresentedCompanies
                    .Where(a => a.IsActive)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (rows.Count == 0)
                {
                    return 0;
                }

                _context.MandateSignerRepresentedCompanies.RemoveRange(rows);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return rows.Count;
            },
            cancellationToken);

    private async Task<IReadOnlyList<RepresentedAssociationImpactRow>> LoadAsync(CancellationToken cancellationToken)
    {
        var assoc = await _context.MandateSignerRepresentedCompanies.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.MandateSignerId, a.TransitOfficeId, a.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assoc.Count == 0)
        {
            return [];
        }

        var signerIds = assoc.Select(a => a.MandateSignerId).Distinct().ToList();
        var representedIds = assoc.Select(a => a.RepresentedCompanyId).Distinct().ToList();
        var officeIds = assoc.Select(a => a.TransitOfficeId).Distinct().ToList();

        var nits = await _context.RepresentedCompanies.AsNoTracking()
            .Where(e => representedIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DocumentNumber })
            .ToDictionaryAsync(e => e.Id, e => e.DocumentNumber, cancellationToken)
            .ConfigureAwait(false);

        var signers = await _context.MandateSigners.AsNoTracking()
            .Where(s => signerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName })
            .ToDictionaryAsync(s => s.Id, s => s.FullName, cancellationToken)
            .ConfigureAwait(false);

        var links = await _context.MandateSignerCompanies.AsNoTracking()
            .Where(c => signerIds.Contains(c.MandateSignerId) && officeIds.Contains(c.TransitOfficeId))
            .Select(c => new { c.MandateSignerId, c.TransitOfficeId, c.CompanyTenantId, c.IsActive })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tenantIds = links.Select(l => l.CompanyTenantId).Distinct().ToList();
        var tenantNames = await _context.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.LegalName })
            .ToDictionaryAsync(t => t.Id, t => t.LegalName, cancellationToken)
            .ConfigureAwait(false);

        var officeNames = await _context.TransitOffices.AsNoTracking()
            .Where(o => officeIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Name })
            .ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken)
            .ConfigureAwait(false);

        // HU #13176b (D2) — el reporte lista EXACTAMENTE lo que el retiro borra: toda fila activa de asociación,
        // sin filtrar por estado del mandatario ni exigir vínculo activo ni NIT resuelto. Se agrupa por
        // mandatario y organismo; la compañía sale del vínculo (activo primero) o queda vacía si ya no hay.
        var result = new List<RepresentedAssociationImpactRow>();
        foreach (var group in assoc.GroupBy(a => new { a.MandateSignerId, a.TransitOfficeId }))
        {
            var represented = group.Select(a => a.RepresentedCompanyId).Distinct().ToList();
            var empresas = represented
                .Select(id => nits.GetValueOrDefault(id)?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var groupLinks = links
                .Where(l => l.MandateSignerId == group.Key.MandateSignerId && l.TransitOfficeId == group.Key.TransitOfficeId)
                .ToList();
            var companyIds = groupLinks
                .OrderByDescending(l => l.IsActive)
                .Select(l => l.CompanyTenantId)
                .Distinct()
                .ToList();
            if (companyIds.Count == 0)
            {
                companyIds.Add(Guid.Empty);
            }
            else if (groupLinks.Any(l => l.IsActive))
            {
                companyIds = [.. groupLinks.Where(l => l.IsActive).Select(l => l.CompanyTenantId).Distinct()];
            }

            foreach (var companyId in companyIds)
            {
                result.Add(new RepresentedAssociationImpactRow(
                    companyId,
                    tenantNames.GetValueOrDefault(companyId) ?? string.Empty,
                    group.Key.TransitOfficeId,
                    officeNames.GetValueOrDefault(group.Key.TransitOfficeId) ?? string.Empty,
                    group.Key.MandateSignerId,
                    signers.GetValueOrDefault(group.Key.MandateSignerId) ?? string.Empty,
                    represented.Count,
                    empresas));
            }
        }

        return result;
    }
}
