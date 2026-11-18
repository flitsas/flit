using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.PersonaNatural;
using Flit.Modules.Security.Application.Products;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Endpoints;

/// <summary>Quién opera la caja en ESTA petición: sale del token y de la base, nunca del cliente.</summary>
internal sealed record ActorCaja(Guid UsuarioId, string Nombre, Guid OrganismoId, string OrganismoCodigo, string OrganismoNombre, bool EsAdmin);

/// <summary>
/// Resuelve el organismo y el nombre del actor de la caja desde la base (sin caché). El usuario <c>ot_caja</c> se revalida por completo
/// (activo, sin suspensión, rol vigente, producto caja encendido, organismo activo); el administrador usa el organismo de su tenant.
/// Las pruebas lo reemplazan por un doble.
/// </summary>
internal interface IActorCajaStore
{
    /// <summary>El actor, o <c>null</c> si ya no puede cobrar en un organismo.</summary>
    Task<ActorCaja?> ResolverAsync(bool esAdmin, Guid usuarioId, Guid tenantId, CancellationToken ct);
}

internal sealed class EfActorCajaStore(FlitDbContext db, IProductAccessResolver acceso) : IActorCajaStore
{
    public async Task<ActorCaja?> ResolverAsync(bool esAdmin, Guid usuarioId, Guid tenantId, CancellationToken ct)
    {
        if (!esAdmin)
        {
            var r = await CajaUsuarios.ResolverAsync(db, acceso, tenantId, usuarioId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            return r.Activo ? new ActorCaja(usuarioId, r.UsuarioNombre, r.OrganismoId, r.OrganismoCodigo, r.OrganismoNombre, EsAdmin: false) : null;
        }

        var organismo = await (from p in db.TransitOfficeProfiles.AsNoTracking()
                               join o in db.TransitOffices.AsNoTracking() on p.TransitOfficeId equals o.Id
                               where p.TenantId == tenantId && o.IsActive
                               select new { o.Id, o.Code, o.Name }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (organismo is null)
            return null;

        var nombre = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new ActorCaja(usuarioId, nombre ?? string.Empty, organismo.Id, organismo.Code, organismo.Name, EsAdmin: true);
    }
}
