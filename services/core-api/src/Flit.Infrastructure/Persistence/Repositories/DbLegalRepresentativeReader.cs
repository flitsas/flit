using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core del directorio de representantes legales por compañía (HU #10900, ADR-0033).
/// Tenant-scoped: corre bajo el contexto RLS del tenant (<see cref="TenantRlsScope"/>). Proyecta la
/// compañía representada (join) y los tipos de trámite del puente. <c>DocumentNumber</c> es PII (Ley
/// 1581): no loguear.
/// </summary>
internal sealed class DbLegalRepresentativeReader : ILegalRepresentativeReader
{
    // Hora de Colombia (UTC-5, sin DST): el estado de vigencia se cuenta por día calendario local,
    // coherente con el resto del cálculo de vigencia de escrituras (ADR-0033).
    private static readonly TimeSpan ColombiaUtcOffset = TimeSpan.FromHours(-5);

    private readonly FlitDbContext _context;
    private readonly TimeProvider _timeProvider;

    public DbLegalRepresentativeReader(FlitDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // TimeProvider es opcional para no romper los ~15 sitios que construyen el reader directamente
        // (tests) ni la resolución por DI (TimeProvider.System está registrado como singleton).
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var baseQuery = _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId);

                var totalCount = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);
                if (totalCount == 0)
                {
                    return PagedResult<LegalRepresentativeItem>.Empty;
                }

                var pageRows = await baseQuery
                    .OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var items = await ProjectAsync(pageRows, cancellationToken).ConfigureAwait(false);
                return new PagedResult<LegalRepresentativeItem>(items, totalCount);
            },
            cancellationToken);

    public Task<LegalRepresentativeItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                // Detalle: incluye el historial de escrituras por compañía (HU #10933).
                var items = await ProjectAsync([row], cancellationToken, includeDeeds: true)
                    .ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(
        Guid tenantId,
        string companyNit,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var nit = companyNit.Trim();
        var docType = documentType.Trim();
        var doc = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                var row = reps.FirstOrDefault(r => r.DocumentType == docType && r.DocumentNumber == doc);
                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        var nit = companyNit.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                var row = reps.FirstOrDefault();
                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<LegalRepresentativeItem>> ListActiveByCompanyNitAsync(
        Guid tenantId,
        string companyNit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyNit);
        var nit = companyNit.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var reps = await ActiveRepsByNitAsync(tenantId, nit, cancellationToken).ConfigureAwait(false);
                return await ProjectAsync(reps, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public Task<LegalRepresentativeItem?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var docType = documentType.Trim();
        var doc = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.CompanyLegalRepresentatives
                    .AsNoTracking()
                    .Where(r => r.TenantId == tenantId
                        && r.IsActive
                        && r.DocumentType == docType
                        && r.DocumentNumber == doc)
                    .OrderByDescending(r => r.CreatedAt)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
                return items.Count == 0 ? null : items[0];
            },
            cancellationToken);
    }

    /// <summary>
    /// Representantes ACTIVOS del tenant asociados a un NIT, por el puente multiempresa (HU #10932) o
    /// por la compañía primaria (compatibilidad). Ordenados por creación descendente.
    /// </summary>
    private async Task<List<CompanyLegalRepresentativeEntity>> ActiveRepsByNitAsync(
        Guid tenantId,
        string nit,
        CancellationToken cancellationToken)
    {
        var companyIds = await _context.RepresentedCompanies
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DocumentNumber == nit)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (companyIds.Count == 0)
        {
            return [];
        }

        var bridgeRepIds = await _context.LegalRepresentativeCompanies
            .AsNoTracking()
            .Where(l => companyIds.Contains(l.RepresentedCompanyId))
            .Select(l => l.RepresentativeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _context.CompanyLegalRepresentatives
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId
                && r.IsActive
                && (bridgeRepIds.Contains(r.Id) || companyIds.Contains(r.RepresentedCompanyId)))
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var rows = await _context.RepresentedCompanies
                    .AsNoTracking()
                    .Where(c => c.TenantId == tenantId)
                    .OrderBy(c => c.Name)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<RepresentedCompanyItem> items = [.. rows.Select(ToCompanyItem)];
                return items;
            },
            cancellationToken);

    public Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(
        Guid tenantId,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        var nit = documentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var row = await _context.RepresentedCompanies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        c => c.TenantId == tenantId && c.DocumentNumber == nit, cancellationToken)
                    .ConfigureAwait(false);

                return row is null ? null : ToCompanyItem(row);
            },
            cancellationToken);
    }

    /// <summary>
    /// Proyecta las filas de representante a su read model resolviendo, en consultas en lote (sin N+1),
    /// la compañía representada y los tipos de trámite del puente. Con <paramref name="includeDeeds"/>
    /// (detalle) también carga el HISTORIAL de escrituras de cada compañía del representante cruzando
    /// <c>company_deed_companies</c> → <c>company_deeds</c> (HU #10933), en un lote adicional.
    /// </summary>
    private async Task<IReadOnlyList<LegalRepresentativeItem>> ProjectAsync(
        List<CompanyLegalRepresentativeEntity> rows,
        CancellationToken cancellationToken,
        bool includeDeeds = false)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var repIds = rows.Select(r => r.Id).ToList();

        // Puente representante ↔ compañía (HU #10932): todas las compañías de cada representante.
        var bridgeRows = await _context.LegalRepresentativeCompanies
            .AsNoTracking()
            .Where(l => repIds.Contains(l.RepresentativeId))
            .Select(l => new { l.RepresentativeId, l.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bridgeByRep = bridgeRows
            .GroupBy(b => b.RepresentativeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RepresentedCompanyId).ToList());

        var companyIds = rows.Select(r => r.RepresentedCompanyId)
            .Concat(bridgeRows.Select(b => b.RepresentedCompanyId))
            .Distinct()
            .ToList();

        var companies = await _context.RepresentedCompanies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken)
            .ConfigureAwait(false);

        var procedureTypeRows = await _context.CompanyLegalRepresentativeProcedureTypes
            .AsNoTracking()
            .Where(p => repIds.Contains(p.RepresentativeId))
            .Select(p => new { p.RepresentativeId, p.ProcedureTypeId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var procedureTypesByRep = procedureTypeRows
            .GroupBy(p => p.RepresentativeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(x => x.ProcedureTypeId)]);

        // Historial de escrituras por compañía (solo en el detalle). Una escritura puede cubrir varias
        // compañías (M:N) → aparece en cada una. Se ordena por VigenciaHasta desc y se anota el estado
        // contra "hoy" en Colombia. Dos consultas en lote (puente + escrituras), sin N+1.
        IReadOnlyDictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>> deedsByCompany = includeDeeds
            ? await LoadDeedsByCompanyAsync(rows[0].TenantId, companyIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();

        return [.. rows.Select(r =>
        {
            companies.TryGetValue(r.RepresentedCompanyId, out var company);
            procedureTypesByRep.TryGetValue(r.Id, out var procedureTypeIds);

            // Compañías del representante: del puente si hay, si no la primaria; la primaria va primero.
            var repCompanyIds = bridgeByRep.TryGetValue(r.Id, out var linked) && linked.Count > 0
                ? linked
                : [r.RepresentedCompanyId];
            IReadOnlyList<LegalRepresentativeCompanySummary> companySummaries =
            [
                .. repCompanyIds
                    .OrderBy(cid => cid == r.RepresentedCompanyId ? 0 : 1)
                    .Where(companies.ContainsKey)
                    .Select(cid =>
                    {
                        var c = companies[cid];
                        var summary = new LegalRepresentativeCompanySummary(c.Id, c.DocumentNumber, c.Name);
                        return deedsByCompany.TryGetValue(cid, out var deeds)
                            ? summary with { Deeds = deeds }
                            : summary;
                    }),
            ];

            return new LegalRepresentativeItem
            {
                Id = r.Id,
                TenantId = r.TenantId,
                RepresentedCompanyId = r.RepresentedCompanyId,
                CompanyDocumentNumber = company?.DocumentNumber ?? string.Empty,
                CompanyName = company?.Name ?? string.Empty,
                DocumentType = r.DocumentType,
                DocumentNumber = r.DocumentNumber,
                FirstLastName = r.FirstLastName,
                SecondLastName = r.SecondLastName,
                Name = r.Name,
                Email = r.Email,
                Address = r.Address,
                City = r.City,
                Phone = r.Phone,
                SignatureVaultId = r.SignatureVaultId,
                IdentityValidationRef = r.IdentityValidationRef,
                ProcedureTypeIds = procedureTypeIds ?? [],
                Companies = companySummaries,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            };
        })];
    }

    /// <summary>
    /// Carga el historial de escrituras (activas y vencidas) de un conjunto de compañías del tenant y lo
    /// agrupa por compañía (HU #10933). Cruza <c>company_deed_companies</c> → <c>company_deeds</c> en dos
    /// consultas en lote. Cada escritura se anota con su estado contra "hoy" en Colombia (UTC-5) y las
    /// listas quedan ordenadas por <c>VigenciaHasta</c> descendente. Una escritura compartida por varias
    /// compañías aparece en la lista de cada una.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>> LoadDeedsByCompanyAsync(
        Guid tenantId,
        List<Guid> companyIds,
        CancellationToken cancellationToken)
    {
        if (companyIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();
        }

        var bridgeRows = await _context.CompanyDeedCompanies
            .AsNoTracking()
            .Where(dc => companyIds.Contains(dc.RepresentedCompanyId))
            .Select(dc => new { dc.DeedId, dc.RepresentedCompanyId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (bridgeRows.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<RepresentativeDeedSummary>>();
        }

        var deedIds = bridgeRows.Select(b => b.DeedId).Distinct().ToList();
        var deeds = await _context.CompanyDeeds
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && deedIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken)
            .ConfigureAwait(false);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(ColombiaUtcOffset).DateTime);

        return bridgeRows
            .Where(b => deeds.ContainsKey(b.DeedId))
            .GroupBy(b => b.RepresentedCompanyId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RepresentativeDeedSummary>)
                [
                    .. g.Select(b => deeds[b.DeedId])
                        .OrderByDescending(d => d.VigenciaHasta)
                        .ThenByDescending(d => d.CreatedAt)
                        .Select(d => RepresentativeDeedSummary.Create(
                            d.Id, d.Description, d.VigenciaDesde, d.VigenciaHasta, d.IsActive, today)),
                ]);
    }

    private static RepresentedCompanyItem ToCompanyItem(RepresentedCompanyEntity c) =>
        new()
        {
            Id = c.Id,
            TenantId = c.TenantId,
            DocumentType = c.DocumentType,
            DocumentNumber = c.DocumentNumber,
            Name = c.Name,
            Email = c.Email,
            Address = c.Address,
            City = c.City,
            Phone = c.Phone,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
}
