using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.TransitOffices.Create;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del alta y listado administrativo de tenants Organismo de
/// Tránsito (OT): <c>identity.tenants</c> (sin RLS) + <c>admin.transit_office_profiles</c>
/// (con RLS por tenant). El rol de sistema <c>ot_admin</c> vive en el catálogo GLOBAL
/// <c>security.roles</c> (HU #10505 / ADR-0023, sin tenant_id) — ya NO se crea una fila de
/// rol por cada OT nuevo (violaría <c>UNIQUE(code, target_entity_type)</c>): la fila global
/// única se siembra por migración/seed y se resuelve por Code en la invitación del primer
/// admin del OT.
///
/// El alta corre dentro de una única transacción con <c>SET LOCAL app.current_tenant_id</c>
/// (mismo patrón que <see cref="OtProfileRepository"/>) para que el INSERT del perfil
/// OT —tabla con RLS— vea el <c>tenant_id</c> recién generado. El listado necesita leer
/// perfiles OT de múltiples tenants (SuperAdmin es cross-tenant por definición), así que
/// usa <c>SET LOCAL row_security = off</c> (mismo patrón que
/// <see cref="OtClientProcedureRepository"/>).
/// </summary>
internal sealed class TransitOfficeTenantWriteRepository : ITransitOfficeTenantWriteRepository
{
    /// <summary>Código del rol de sistema del módulo OT (no se renombra — ver ADR del refactor adminOT).</summary>
    internal const string OtAdminRoleCode = "ot_admin";

    private readonly FlitDbContext _context;

    public TransitOfficeTenantWriteRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        _context.Tenants.AsNoTracking().AnyAsync(t => t.Code == code, cancellationToken);

    public Task<bool> TransitOfficeHasProfileAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            () => _context.TransitOfficeProfiles
                .AsNoTracking()
                .AnyAsync(p => p.TransitOfficeId == transitOfficeId, cancellationToken),
            cancellationToken);

    public Task<TransitOfficeTenantItem> CreateAsync(
        NewTransitOffice newTransitOffice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTransitOffice);

        return ExecuteInNewTenantScopeAsync(
            async tenantId =>
            {
                var now = DateTimeOffset.UtcNow;

                var tenant = new Tenant
                {
                    Id = tenantId,
                    Code = newTransitOffice.Code,
                    LegalName = newTransitOffice.LegalName,
                    TaxId = newTransitOffice.TaxId,
                    // Decisión intencional (no se toca en HUs futuras sin revisar el ADR):
                    // no existe un tenant_type dedicado para OT — la migración
                    // 20260630180000_RestrictTenantTypeCatalog restringió tenant_type al
                    // catálogo B2B (RENTING | CONCESIONARIO | FLIT). TransitOfficeProfile
                    // sigue siendo la única fuente de verdad de que un tenant "es" un OT.
                    TenantType = Flit.Admin.Domain.Companies.Create.CompanyTenantTypes.Renting,
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = newTransitOffice.CreatedBy,
                };
                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // HU #10505 / ADR-0023: "ot_admin" es ahora un rol del catálogo GLOBAL
                // (security.roles ya no tiene tenant_id — UNIQUE(code, target_entity_type)). Ya
                // NO se crea una fila de rol por cada OT nuevo (violaría esa unicidad); el
                // catálogo trae una única fila "ot_admin" (target_entity_type = TRANSIT_OFFICE)
                // sembrada por migración/seed, sin RoleGrants (el SuperAdmin los cura vía RBAC
                // Admin, comportamiento sin cambios). La invitación del primer admin del OT
                // (POST /api/v1/security/invitations, POST /api/v1/admin/ot/users/invite)
                // resuelve ese rol global por Code, no por tenant.

                var profile = new TransitOfficeProfile
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TransitOfficeId = newTransitOffice.TransitOfficeId,
                    OperationMode = newTransitOffice.OperationMode,
                    QuipuxReadOnly = false,
                    CreatedAt = now,
                    CreatedBy = newTransitOffice.CreatedBy,
                };
                _context.TransitOfficeProfiles.Add(profile);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var office = await _context.TransitOffices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == newTransitOffice.TransitOfficeId, cancellationToken)
                    .ConfigureAwait(false);

                return Project(tenant, profile, office);
            },
            cancellationToken);
    }

    public async Task<PagedResult<TransitOfficeTenantItem>> ListAsync(
        TransitOfficeTenantListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var query =
                    from t in _context.Tenants.AsNoTracking()
                    join p in _context.TransitOfficeProfiles.AsNoTracking() on t.Id equals p.TenantId
                    select new { Tenant = t, Profile = p };

                if (!string.IsNullOrWhiteSpace(filter.LegalName))
                {
                    // ToLower() (no ToLowerInvariant) para que Npgsql lo traduzca a lower() en
                    // SQL; ToLowerInvariant no es traducible y rompe en PostgreSQL real.
                    var legalName = filter.LegalName.Trim().ToLower();
                    query = query.Where(x => x.Tenant.LegalName.ToLower().Contains(legalName));
                }

                if (filter.EstadoActivo is { } estado)
                {
                    query = query.Where(x => x.Tenant.IsActive == estado);
                }

                var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

                if (totalCount == 0)
                {
                    return PagedResult<TransitOfficeTenantItem>.Empty;
                }

                var page = await query
                    .OrderByDescending(x => x.Tenant.CreatedAt)
                    .ThenByDescending(x => x.Tenant.Id)
                    .Skip((filter.Page - 1) * filter.PageSize)
                    .Take(filter.PageSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var officeIds = page.Select(x => x.Profile.TransitOfficeId).Distinct().ToList();
                var offices = await _context.TransitOffices
                    .AsNoTracking()
                    .Where(o => officeIds.Contains(o.Id))
                    .ToDictionaryAsync(o => o.Id, cancellationToken)
                    .ConfigureAwait(false);

                var items = page
                    .Select(x => Project(x.Tenant, x.Profile, offices.GetValueOrDefault(x.Profile.TransitOfficeId)))
                    .ToList();

                return new PagedResult<TransitOfficeTenantItem>(items, totalCount);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static TransitOfficeTenantItem Project(
        Tenant tenant,
        TransitOfficeProfile profile,
        TransitOffice? office) => new()
        {
            Id = tenant.Id,
            LegalName = tenant.LegalName,
            TaxId = tenant.TaxId,
            Code = tenant.Code,
            TenantType = tenant.TenantType,
            EstadoActivo = tenant.IsActive,
            FechaCreacion = tenant.CreatedAt,
            RowVersion = tenant.RowVersion,
            TransitOfficeId = profile.TransitOfficeId,
            TransitOfficeName = office?.Name ?? string.Empty,
            TransitOfficeCode = office?.Code ?? string.Empty,
            OperationMode = profile.OperationMode,
        };

    /// <summary>
    /// Ejecuta el alta compuesta dentro de una única transacción, fijando
    /// <c>app.current_tenant_id</c> al tenant recién generado (mismo patrón que
    /// <see cref="OtProfileRepository"/>) para que el INSERT del perfil OT —tabla con
    /// RLS por tenant— pase la política. El id del tenant se genera aquí (antes de la
    /// transacción no existe fila ni necesita existir: <c>set_config</c> solo exige un
    /// UUID válido, no una FK existente en ese momento).
    /// </summary>
    private async Task<T> ExecuteInNewTenantScopeAsync<T>(
        Func<Guid, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var tenantId = Guid.NewGuid();

        if (!_context.Database.IsRelational())
        {
            return await action(tenantId).ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                var result = await action(tenantId).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
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
