using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IMandateSignerDirectory"/> (ADR-0036 §D9, HU #10916). Consulta
/// los mandatarios (<c>admin.mandate_signers</c>) ACTIVOS asignados a una compañía gestora en un OT
/// (join con <c>admin.mandate_signer_companies</c> activo). Tablas de administración por OT (sin
/// <c>tenant_id</c> en <c>mandate_signers</c>; <c>company_tenant_id</c> en la de asignación), leídas
/// directo (mismo criterio que <see cref="MandateRequirementPolicy"/>). Devuelve solo los datos que el
/// PDF/selección necesitan (nombre, documento, cuenta de usuario) — no expone PII adicional.
/// </summary>
internal sealed class MandateSignerDirectory : IMandateSignerDirectory
{
    private readonly FlitDbContext _context;

    public MandateSignerDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId, Guid companyTenantId, CancellationToken cancellationToken = default)
    {
        if (transitOfficeId == Guid.Empty || companyTenantId == Guid.Empty)
        {
            return [];
        }

        return await (
            from s in _context.MandateSigners.AsNoTracking()
            join c in _context.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == transitOfficeId
                && c.CompanyTenantId == companyTenantId
                && c.IsActive
                && s.IsActive
            select new MandateSignerCandidate(s.Id, s.FullName, s.DocumentNumber, s.UserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, CancellationToken cancellationToken = default)
    {
        if (mandateSignerId == Guid.Empty)
        {
            return null;
        }

        return await _context.MandateSigners.AsNoTracking()
            .Where(s => s.Id == mandateSignerId && s.IsActive)
            .Select(s => new MandateSignerCandidate(s.Id, s.FullName, s.DocumentNumber, s.UserId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
