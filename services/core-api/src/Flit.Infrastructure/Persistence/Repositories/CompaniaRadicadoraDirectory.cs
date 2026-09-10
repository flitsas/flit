using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bug #11612 — implementación EF Core de <see cref="ICompaniaRadicadoraDirectory"/>: la razón social
/// de la compañía radicadora sale de <c>identity.tenants.legal_name</c>, la MISMA fuente que usan el
/// listado administrativo de compañías (<c>CompanyReadRepository</c>) y la bandeja del organismo
/// (<c>OtClientProcedureRepository.EnrichDisplayNamesAsync</c>).
///
/// <para>Multi-tenant: la consulta filtra por el id del tenant del propio trámite
/// (<c>WHERE id = @tenantId</c>) y proyecta un único nombre; no hay listado ni búsqueda, así que no
/// puede devolver datos de otra compañía. <c>identity.tenants</c> no tiene RLS (ver
/// <c>CompanyReadRepository</c>), por eso el filtro explícito es el aislamiento real.</para>
/// </summary>
internal sealed class CompaniaRadicadoraDirectory : ICompaniaRadicadoraDirectory
{
    private readonly FlitDbContext _context;

    public CompaniaRadicadoraDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string?> GetRazonSocialAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        var legalName = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.LegalName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim();
    }
}
