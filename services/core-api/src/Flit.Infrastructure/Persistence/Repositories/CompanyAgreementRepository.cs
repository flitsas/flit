using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Convenio comercial compañía ↔ organismo sobre <c>admin.company_transit_office_agreements</c>.
///
/// <para>La tabla no lleva RLS a propósito —se consulta desde los dos lados: el admin de la compañía la
/// marca y el generador del documento la lee en el tenant del trámite—, así que el acceso es directo,
/// igual que en <c>mandate_signers</c> y sus puentes.</para>
/// </summary>
internal sealed class CompanyAgreementRepository : ICompanyAgreementRepository
{
    private readonly FlitDbContext _context;

    public CompanyAgreementRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> SetAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        bool isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        if (companyTenantId == Guid.Empty || transitOfficeId == Guid.Empty)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _context.CompanyTransitOfficeAgreements
            .FirstOrDefaultAsync(
                a => a.CompanyTenantId == companyTenantId && a.TransitOfficeId == transitOfficeId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            // Desmarcar un convenio que nunca existió es un no-op: crear la fila solo para dejarla
            // inactiva ensuciaría la tabla sin aportar traza de nada.
            if (!isActive)
            {
                return false;
            }

            _context.CompanyTransitOfficeAgreements.Add(new CompanyTransitOfficeAgreement
            {
                Id = Guid.NewGuid(),
                CompanyTenantId = companyTenantId,
                TransitOfficeId = transitOfficeId,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = changedBy,
            });
        }
        else
        {
            if (existing.IsActive == isActive)
            {
                return true; // Idempotente.
            }

            existing.IsActive = isActive;
            existing.UpdatedAt = now;
            existing.UpdatedBy = changedBy;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> ListActiveOfficeIdsAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default) =>
        companyTenantId == Guid.Empty
            ? []
            : await _context.CompanyTransitOfficeAgreements.AsNoTracking()
                .Where(a => a.CompanyTenantId == companyTenantId && a.IsActive)
                .Select(a => a.TransitOfficeId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
}
