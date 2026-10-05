using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13247 (Feature #13245) — mandatarios Persona natural activos (sin baja lógica) con forma de firma biometría (o legado
/// sin forma ni baúl vinculado: biometría efectiva) cuya identidad propia NO es aprobada. Misma fuente que la firma
/// (<see cref="IdentityVigenciaPorDocumentoResolver.ResolveMandatariosAsync"/>), así que el reporte y el bloqueo nunca
/// discrepan. Modelo: <see cref="DbPhysicalSignatureMigrationReader"/>. Solo lectura; sin backfill.
/// </summary>
internal sealed class DbMandateIdentityAffectedReader : IMandateIdentityAffectedReader
{
    private readonly FlitDbContext _context;
    private readonly IdentityVigenciaPorDocumentoResolver _identityResolver;

    public DbMandateIdentityAffectedReader(
        FlitDbContext context,
        IdentityVigenciaPorDocumentoResolver? identityResolver = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _identityResolver = identityResolver
            ?? new IdentityVigenciaPorDocumentoResolver(new ProcedureInstanceRepository(context));
    }

    public async Task<IReadOnlyList<MandateIdentityAffectedRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MandateSigners.AsNoTracking()
            .Where(s => s.IsActive
                && s.DeletedAt == null
                && s.SignerModel == MandateSignerModels.Natural
                && (s.SignatureMethod == MandateSignatureMethods.Biometria
                    || (s.SignatureMethod == null && s.SignatureVaultId == null)));
        if (transitOfficeId is { } officeFilter)
        {
            query = query.Where(s => s.TransitOfficeId == officeFilter);
        }

        var signers = await query
            .Select(s => new { s.Id, s.FullName, s.Email, s.TransitOfficeId, s.DocumentType, s.DocumentNumber })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (signers.Count == 0)
        {
            return [];
        }

        var identidades = await _identityResolver.ResolveMandatariosAsync(
            [.. signers.Select(s => new IdentityVigenciaPorDocumentoResolver.MandatarioIdentityRef(
                s.Id, s.DocumentType, s.DocumentNumber))],
            DateTimeOffset.UtcNow,
            cancellationToken).ConfigureAwait(false);

        var afectados = signers
            .Where(s => StatusOf(identidades, s.Id) != IdentityVigenciaEstados.AprobadaVigente)
            .ToList();
        if (afectados.Count == 0)
        {
            return [];
        }

        var ids = afectados.Select(s => s.Id).ToList();
        var officeIds = afectados.Select(s => s.TransitOfficeId).Distinct().ToList();

        var links = await _context.MandateSignerCompanies.AsNoTracking()
            .Where(c => ids.Contains(c.MandateSignerId) && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.TransitOfficeId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var offices = await _context.TransitOffices.AsNoTracking()
            .Where(o => officeIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Code, o.Name })
            .ToDictionaryAsync(o => o.Id, cancellationToken)
            .ConfigureAwait(false);
        var tenantNames = await DbPhysicalSignatureMigrationReader
            .LoadTenantNamesAsync(_context, [.. links.Select(l => l.CompanyTenantId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<MandateIdentityAffectedRow>();
        foreach (var s in afectados)
        {
            var estado = Legacy(StatusOf(identidades, s.Id));
            var oficina = offices.GetValueOrDefault(s.TransitOfficeId);
            var code = oficina?.Code ?? string.Empty;
            var name = oficina?.Name ?? string.Empty;
            var companias = links
                .Where(l => l.MandateSignerId == s.Id && l.TransitOfficeId == s.TransitOfficeId)
                .Select(l => l.CompanyTenantId)
                .Distinct()
                .ToList();

            if (companias.Count == 0)
            {
                rows.Add(new MandateIdentityAffectedRow(
                    s.Id, s.FullName, s.Email, null, null, s.TransitOfficeId, code, name, estado));
                continue;
            }

            rows.AddRange(companias.Select(c => new MandateIdentityAffectedRow(
                s.Id, s.FullName, s.Email, c, tenantNames.GetValueOrDefault(c), s.TransitOfficeId, code, name, estado)));
        }

        return rows;
    }

    private static string StatusOf(IReadOnlyDictionary<Guid, IdentityVigenciaResult> map, Guid id) =>
        map.TryGetValue(id, out var r) ? r.Status : IdentityVigenciaEstados.SinValidacion;

    private static string Legacy(string status) => status switch
    {
        IdentityVigenciaEstados.EnCurso => AdminIdentityVigencia.Pending,
        IdentityVigenciaEstados.Vencida => AdminIdentityVigencia.Expired,
        _ => AdminIdentityVigencia.None,
    };
}
