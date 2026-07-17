using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escritura EF Core de la parametrización Quipux del catálogo (HU #10710):
/// <c>catalogs.transit_offices.divipo_code</c> + banderas por familia de trámite.
///
/// <c>catalogs.transit_offices</c> es un catálogo GLOBAL sin RLS (ver
/// <c>33-HU10710-quipux-external-refs-seed.sql</c>), así que —a diferencia de
/// <see cref="DbTransitOfficeOperationalStatusReader"/>, que sí atraviesa el perfil OT con RLS
/// por tenant— no hace falta <c>SET LOCAL row_security = off</c>. El control de acceso es la
/// SuperAdminPolicy del endpoint.
/// </summary>
internal sealed class DbTransitOfficeQuipuxSettingsWriter : ITransitOfficeQuipuxSettingsWriter
{
    private readonly FlitDbContext _context;

    public DbTransitOfficeQuipuxSettingsWriter(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> UpdateQuipuxSettingsAsync(
        Guid transitOfficeId,
        string? divipoCode,
        bool quipuxRegistration,
        bool quipuxTransfer,
        bool quipuxOther,
        CancellationToken cancellationToken = default)
    {
        // Se materializa la entidad (en vez de ExecuteUpdate) para que funcione igual en el
        // proveedor InMemory de los tests.
        var office = await _context.TransitOffices
            .FirstOrDefaultAsync(o => o.Id == transitOfficeId && o.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (office is null)
        {
            return false;
        }

        office.DivipoCode = divipoCode;
        office.QuipuxRegistration = quipuxRegistration;
        office.QuipuxTransfer = quipuxTransfer;
        office.QuipuxOther = quipuxOther;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
