using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Radicados;

/// <summary>
/// Bloqueo por radicado tras fallos seguidos, sobre la misma tabla y las mismas opciones que «reanudar» (HU #14035/#14042):
/// quien adivina cédulas por una de las dos rutas gasta el mismo cupo que por la otra. La clave es la huella del radicado,
/// exista o no, para no revelar cuáles existen.
/// </summary>
public sealed class BloqueoPorRadicado(PersonaNaturalDb db, TimeProvider reloj, IOptions<SesionOptions> options)
{
    private readonly SesionOptions _opciones = options.Value;

    /// <summary>Cuánto falta para que se libere el radicado, o <c>null</c> si no está bloqueado.</summary>
    public async Task<TimeSpan?> EsperaAsync(byte[] clave, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var ventana = TimeSpan.FromMinutes(_opciones.VentanaIntentosMinutos);
        var desde = ahora - ventana;
        var intentos = await db.Set<IntentoReanudar>().Where(i => i.Clave == clave && i.OcurrioEn > desde)
            .OrderBy(i => i.OcurrioEn).Select(i => i.OcurrioEn).ToListAsync(ct).ConfigureAwait(false);
        if (intentos.Count < _opciones.MaxIntentosFallidos)
            return null;

        var libera = intentos[intentos.Count - _opciones.MaxIntentosFallidos] + ventana;
        return libera > ahora ? libera - ahora : TimeSpan.FromSeconds(1);
    }

    public async Task RegistrarFalloAsync(byte[] clave, CancellationToken ct)
    {
        db.Add(new IntentoReanudar { Clave = clave, OcurrioEn = reloj.GetUtcNow() });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
