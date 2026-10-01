using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13135 (Feature #13115) — impacto de dar de baja a un mandatario, de solo lectura y cross-tenant
/// (<c>SET LOCAL row_security = off</c>, igual que <see cref="DbMandateSignerReader"/>): compañías y organismos donde
/// es el ÚNICO activo de esa compañía, defaults (de compañía y del organismo) que perdería y trámites radicados sin
/// aprobar que hoy lo usan. Solo identificadores y conteos: nada de datos personales.
/// </summary>
internal sealed class DbMandateSignerImpactReader : IMandateSignerImpactReader
{
    private readonly FlitDbContext _context;

    public DbMandateSignerImpactReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<MandateSignerImpact> GetImpactAsync(
        Guid mandateSignerId,
        CancellationToken cancellationToken = default) =>
        ExecuteCrossTenantReadAsync(
            async () =>
            {
                // Compañías y organismos donde es el ÚNICO activo: sin otro mandatario vivo y activo vinculado.
                var activeLinks = await _context.MandateSignerCompanies
                    .AsNoTracking()
                    .Where(c => c.MandateSignerId == mandateSignerId && c.IsActive)
                    .Select(c => new { c.TransitOfficeId, c.CompanyTenantId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var onlyActive = new List<MandateSignerLinkRef>();
                if (activeLinks.Count > 0)
                {
                    var officeIds = activeLinks.Select(l => l.TransitOfficeId).Distinct().ToList();
                    var companyIds = activeLinks.Select(l => l.CompanyTenantId).Distinct().ToList();

                    var others = await (
                        from c in _context.MandateSignerCompanies.AsNoTracking()
                        join s in _context.MandateSigners.AsNoTracking() on c.MandateSignerId equals s.Id
                        where c.IsActive
                            && c.MandateSignerId != mandateSignerId
                            && s.IsActive
                            && s.DeletedAt == null
                            && officeIds.Contains(c.TransitOfficeId)
                            && companyIds.Contains(c.CompanyTenantId)
                        select new { c.TransitOfficeId, c.CompanyTenantId })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    var covered = others.Select(o => (o.TransitOfficeId, o.CompanyTenantId)).ToHashSet();
                    onlyActive =
                    [
                        .. activeLinks
                            .Where(l => !covered.Contains((l.TransitOfficeId, l.CompanyTenantId)))
                            .Select(l => new MandateSignerLinkRef(l.TransitOfficeId, l.CompanyTenantId)),
                    ];
                }

                var defaults = new List<MandateSignerDefaultRef>();
                defaults.AddRange(
                    (await _context.CompanyOtMandateRules
                        .AsNoTracking()
                        .Where(r => r.DefaultMandateSignerId == mandateSignerId)
                        .Select(r => new { r.TransitOfficeId, r.CompanyTenantId })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .Select(r => new MandateSignerDefaultRef(
                        MandateSignerDefaultRef.CompanyRule, r.TransitOfficeId, r.CompanyTenantId)));
                defaults.AddRange(
                    (await _context.TransitOfficeMandateConfigs
                        .AsNoTracking()
                        .Where(c => c.DefaultMandateSignerId == mandateSignerId)
                        .Select(c => c.TransitOfficeId)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .Select(officeId => new MandateSignerDefaultRef(MandateSignerDefaultRef.Office, officeId, null)));

                // Trámites radicados sin aprobar que hoy apuntan al mandatario (los que la baja reasignaría).
                var pendientes = Flit.Tramites.Domain.Tramites.Estados.TramiteEstado.PendientesDelOrganismo.ToArray();
                var pending = await _context.ProcedureInstances
                    .AsNoTracking()
                    .CountAsync(
                        p => p.MandateSignerId == mandateSignerId
                            && p.DeletedAt == null
                            && pendientes.Contains(p.Status),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new MandateSignerImpact(onlyActive, defaults, pending);
            },
            cancellationToken);

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await action().ConfigureAwait(false);
        }

        // Dentro de una transacción ya abierta no se puede anidar otra: el SET LOCAL muere con su commit.
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
