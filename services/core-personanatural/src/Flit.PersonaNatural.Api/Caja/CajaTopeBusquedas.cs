using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Tope de búsquedas sin resultado por usuario (ADR-0082, enumeración de IDs <c>PN-AAAA-NNNNNN</c>): 10 en 5 minutos por defecto.
/// El conteo sale de <c>cashier_event</c>, no de memoria, así que vale con varias réplicas y sobrevive a un reinicio.
/// </summary>
public sealed class CajaTopeBusquedas(PersonaNaturalDb db, IOptions<CajaOptions> opciones, TimeProvider reloj)
{
    /// <summary>Cuánto debe esperar el usuario si ya llegó al tope; <c>null</c> si puede seguir buscando.</summary>
    public async Task<TimeSpan?> EsperaAsync(Guid usuarioId, CancellationToken ct)
    {
        var o = opciones.Value;
        var ventana = TimeSpan.FromMinutes(Math.Max(1, o.BusquedasFallidasVentanaMin));
        var ahora = reloj.GetUtcNow();
        var desde = ahora - ventana;
        var fallidas = await db.Set<EventoCaja>().AsNoTracking()
            .Where(e => e.UsuarioId == usuarioId && e.Accion == EventoCaja.AccionBusquedaFallida && e.CreadoEn > desde)
            .OrderBy(e => e.CreadoEn).Select(e => e.CreadoEn).ToListAsync(ct).ConfigureAwait(false);
        var max = Math.Max(1, o.BusquedasFallidasMax);
        if (fallidas.Count < max)
            return null;

        // Se libera cuando la fallida que deja el conteo por debajo del tope sale de la ventana.
        var libera = fallidas[fallidas.Count - max] + ventana;
        var espera = libera - ahora;
        return espera < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : espera;
    }
}
