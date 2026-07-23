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
    private readonly FlitDbContext _context;

    public DbLegalRepresentativeReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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

                var items = await ProjectAsync([row], cancellationToken).ConfigureAwait(false);
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
                var row = await (
                    from r in _context.CompanyLegalRepresentatives.AsNoTracking()
                    join c in _context.RepresentedCompanies.AsNoTracking()
                        on r.RepresentedCompanyId equals c.Id
                    where r.TenantId == tenantId
                        && r.IsActive
                        && r.DocumentType == docType
                        && r.DocumentNumber == doc
                        && c.DocumentNumber == nit
                    select r)
                    .OrderByDescending(r => r.CreatedAt)
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
                var row = await (
                    from r in _context.CompanyLegalRepresentatives.AsNoTracking()
                    join c in _context.RepresentedCompanies.AsNoTracking()
                        on r.RepresentedCompanyId equals c.Id
                    where r.TenantId == tenantId
                        && r.IsActive
                        && c.DocumentNumber == nit
                    select r)
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
    /// Proyecta las filas de representante a su read model resolviendo, en dos consultas en lote (sin
    /// N+1), la compañía representada y los tipos de trámite del puente.
    /// </summary>
    private async Task<IReadOnlyList<LegalRepresentativeItem>> ProjectAsync(
        List<CompanyLegalRepresentativeEntity> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var repIds = rows.Select(r => r.Id).ToList();
        var companyIds = rows.Select(r => r.RepresentedCompanyId).Distinct().ToList();

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

        return [.. rows.Select(r =>
        {
            companies.TryGetValue(r.RepresentedCompanyId, out var company);
            procedureTypesByRep.TryGetValue(r.Id, out var procedureTypeIds);
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
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            };
        })];
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
