using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core de mandatarios y compañías de un organismo de tránsito (ADR-0023).
/// <c>admin.mandate_signers</c> y <c>admin.mandate_signer_companies</c> se acotan en la capa
/// de aplicación por <c>transit_office_id</c>; <c>admin.tenant_transit_office_grants</c> e
/// <c>identity.tenants</c> tienen RLS por tenant y aquí se leen cross-tenant (gestión OT del
/// SuperAdmin / ot_admin) con <c>SET LOCAL row_security = off</c>, igual que
/// <see cref="DbTransitOfficeOperationalStatusReader"/>. Las fuentes se materializan por
/// separado y se combinan en memoria para comportarse igual en PostgreSQL e InMemory.
/// </summary>
internal sealed class DbMandateSignerReader : IMandateSignerReader
{
    private readonly FlitDbContext _context;

    public DbMandateSignerReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<MandateSignerItem>> ListByOtAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                // Activos e inactivos (baja lógica): los inactivados siguen visibles para
                // poder reactivarlos. Se muestran primero los activos, luego por nombre.
                var signers = await _context.MandateSigners
                    .AsNoTracking()
                    .Where(s => s.TransitOfficeId == transitOfficeId)
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.FullName)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var companiesBySigner = await LoadActiveCompanyIdsBySignerAsync(
                    transitOfficeId, cancellationToken).ConfigureAwait(false);

                var vigenciaBySigner = await LoadIdentityVigenciaAsync(
                    [.. signers.Select(s => s.Id)], cancellationToken).ConfigureAwait(false);

                IReadOnlyList<MandateSignerItem> items =
                [
                    .. signers.Select(s => Project(s, companiesBySigner, vigenciaBySigner)),
                ];
                return items;
            },
            cancellationToken);

    public Task<MandateSignerItem?> GetByIdAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var signer = await _context.MandateSigners
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == mandateSignerId, cancellationToken)
                    .ConfigureAwait(false);

                if (signer is null)
                {
                    return null;
                }

                var companyIds = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.MandateSignerId == mandateSignerId && c.IsActive)
                    .Select(c => c.CompanyTenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var vigenciaBySigner = await LoadIdentityVigenciaAsync(
                    [signer.Id], cancellationToken).ConfigureAwait(false);

                return new MandateSignerItem
                {
                    Id = signer.Id,
                    TransitOfficeId = signer.TransitOfficeId,
                    FullName = signer.FullName,
                    DocumentType = signer.DocumentType,
                    DocumentNumber = signer.DocumentNumber,
                    IntegrityHash = signer.IntegrityHash,
                    Email = signer.Email,
                    SignatureVaultId = signer.SignatureVaultId,
                    IdentityValidationRef = signer.IdentityValidationRef,
                    IdentityStatus = vigenciaBySigner
                        .GetValueOrDefault(signer.Id, new AdminIdentityVigencia.Resultado(
                            AdminIdentityVigencia.None, null)).Status,
                    IdentityValidUntil = vigenciaBySigner
                        .GetValueOrDefault(signer.Id, new AdminIdentityVigencia.Resultado(
                            AdminIdentityVigencia.None, null)).ValidUntil,
                    UserId = signer.UserId,
                    RegisteredAt = signer.RegisteredAt,
                    IsActive = signer.IsActive,
                    CompanyTenantIds = companyIds,
                };
            },
            cancellationToken);

    public Task<IReadOnlyList<OtCompanyOption>> ListOtCompaniesAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var grants = await _context.TenantTransitOfficeGrants
                    .AsNoTracking()
                    .Where(g => g.TransitOfficeId == transitOfficeId)
                    .Select(g => new { g.TenantId, g.IsEnabled })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tenantIds = grants.Select(g => g.TenantId).Distinct().ToList();
                var tenants = await _context.Tenants
                    .AsNoTracking()
                    .Where(t => tenantIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.LegalName, t.IsActive })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var tenantById = tenants.ToDictionary(t => t.Id);

                IReadOnlyList<OtCompanyOption> options =
                [
                    .. grants
                        .Where(g => tenantById.ContainsKey(g.TenantId))
                        .Select(g =>
                        {
                            var tenant = tenantById[g.TenantId];
                            return new OtCompanyOption
                            {
                                CompanyTenantId = tenant.Id,
                                LegalName = tenant.LegalName,
                                IsActive = tenant.IsActive,
                                IsEnabled = g.IsEnabled,
                            };
                        })
                        .OrderBy(o => o.LegalName),
                ];
                return options;
            },
            cancellationToken);

    public Task<IReadOnlyList<MandateSignerCompanyResolution>> ListActiveCompanyResolutionsAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                var signers = await _context.MandateSigners
                    .AsNoTracking()
                    .Where(s => s.TransitOfficeId == transitOfficeId && s.IsActive)
                    .Select(s => new { s.Id, s.FullName, s.IntegrityHash })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var signerById = signers.ToDictionary(s => s.Id);

                var assignments = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.TransitOfficeId == transitOfficeId && c.IsActive)
                    .Select(c => new { c.CompanyTenantId, c.MandateSignerId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<MandateSignerCompanyResolution> resolutions =
                [
                    .. assignments
                        .Where(a => signerById.ContainsKey(a.MandateSignerId))
                        .Select(a =>
                        {
                            var signer = signerById[a.MandateSignerId];
                            return new MandateSignerCompanyResolution
                            {
                                CompanyTenantId = a.CompanyTenantId,
                                MandateSignerId = signer.Id,
                                FullName = signer.FullName,
                                IntegrityHash = signer.IntegrityHash,
                            };
                        }),
                ];
                return resolutions;
            },
            cancellationToken);

    private async Task<Dictionary<Guid, List<Guid>>> LoadActiveCompanyIdsBySignerAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken)
    {
        var rows = await _context.MandateSignerCompanies
            .AsNoTracking()
            .Where(c => c.TransitOfficeId == transitOfficeId && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.MandateSignerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.CompanyTenantId).ToList());
    }

    private static MandateSignerItem Project(
        Entities.Admin.MandateSigner signer,
        Dictionary<Guid, List<Guid>> companiesBySigner,
        Dictionary<Guid, AdminIdentityVigencia.Resultado> vigenciaBySigner)
    {
        var vigencia = vigenciaBySigner.GetValueOrDefault(
            signer.Id, new AdminIdentityVigencia.Resultado(AdminIdentityVigencia.None, null));

        return new MandateSignerItem
        {
            Id = signer.Id,
            TransitOfficeId = signer.TransitOfficeId,
            FullName = signer.FullName,
            DocumentType = signer.DocumentType,
            DocumentNumber = signer.DocumentNumber,
            IntegrityHash = signer.IntegrityHash,
            Email = signer.Email,
            SignatureVaultId = signer.SignatureVaultId,
            IdentityValidationRef = signer.IdentityValidationRef,
            IdentityStatus = vigencia.Status,
            IdentityValidUntil = vigencia.ValidUntil,
            UserId = signer.UserId,
            RegisteredAt = signer.RegisteredAt,
            IsActive = signer.IsActive,
            CompanyTenantIds = companiesBySigner.GetValueOrDefault(signer.Id, []),
        };
    }

    /// <summary>
    /// Vigencia de la identidad (HU #10994) por mandatario. La precedencia vive en
    /// <see cref="AdminIdentityVigencia"/>, compartida con el representante legal (HU #11059): aquí solo
    /// queda la consulta en lote. Desde la HU #11060 se devuelve TAMBIÉN hasta cuándo es válida, que es
    /// lo que la consola necesita para informar la vigencia en curso en vez de ofrecer renovar.
    /// Se lee dentro del scope cross-tenant (row_security off) que abre <c>ExecuteCrossTenantReadAsync</c>.
    /// </summary>
    private async Task<Dictionary<Guid, AdminIdentityVigencia.Resultado>> LoadIdentityVigenciaAsync(
        IReadOnlyList<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        if (signerIds.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var rows = await _context.AdminIdentityValidations
            .AsNoTracking()
            .Where(v => v.SubjectType == AdminIdentitySubjectTypes.MandateSigner
                && signerIds.Contains(v.SubjectRef))
            .Select(v => new { v.SubjectRef, v.Status, v.ValidUntil })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.SubjectRef)
            .ToDictionary(
                g => g.Key,
                g => AdminIdentityVigencia.Resumir(
                    g.Select(r => new AdminIdentityVigencia.Entrada(r.Status, r.ValidUntil)), now));
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        // HU #11000 — misma guarda que MandateSignerDirectory (HU #10992) y PlateRangeRepository: dentro de
        // una transacción ya abierta NO se puede anidar otra ("The connection is already in a transaction").
        // Se aplica el SET LOCAL sobre la transacción en curso; muere con su commit.
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "SET LOCAL row_security = off", cancellationToken).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "SET LOCAL row_security = off",
                    cancellationToken).ConfigureAwait(false);

                var result = await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
