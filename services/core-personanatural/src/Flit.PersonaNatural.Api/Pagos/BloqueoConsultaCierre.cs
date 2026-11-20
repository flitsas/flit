using System.Security.Cryptography;
using System.Text;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Freno a la enumeración de códigos públicos en <c>GET /punto/pago/cierre/{codigo}</c> (los códigos son secuenciales). Cuenta los
/// fallos seguidos (trámite que no existe, de otro organismo o sin pago: los tres responden el mismo 404) por cookie del punto
/// (organismo) y por IP; al llegar a <see cref="MaxFallos"/> dentro de <see cref="Ventana"/> bloquea esa combinación por la ventana
/// completa. Un acierto borra los fallos. Reutiliza la tabla de intentos de reanudar (<c>resume_attempt</c>) con una clave propia
/// derivada de organismo + IP: sin cambiar el esquema y con el mismo barrido de retención. El tope por minuto lo da la política
/// <see cref="Abuso.AbusoEndpoints.PoliticaCierrePorCodigo"/>.
/// </summary>
public sealed class BloqueoConsultaCierre(PersonaNaturalDb db, TimeProvider reloj)
{
    public const int MaxFallos = 5;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);

    /// <summary>Cuánto falta para que se libere el bloqueo, o <c>null</c> si puede consultar.</summary>
    public async Task<TimeSpan?> EsperaAsync(Guid organismoId, byte[]? ipHash, CancellationToken ct)
    {
        var clave = Clave(organismoId, ipHash);
        var ahora = reloj.GetUtcNow();
        var desde = ahora - Ventana;
        var fallos = await db.Set<IntentoReanudar>().AsNoTracking().Where(i => i.Clave == clave && i.OcurrioEn > desde)
            .OrderBy(i => i.OcurrioEn).Select(i => i.OcurrioEn).ToListAsync(ct).ConfigureAwait(false);
        if (fallos.Count < MaxFallos)
            return null;
        var libera = fallos[fallos.Count - MaxFallos] + Ventana;
        return libera > ahora ? libera - ahora : TimeSpan.FromSeconds(1);
    }

    public async Task RegistrarFalloAsync(Guid organismoId, byte[]? ipHash, CancellationToken ct)
    {
        db.Add(new IntentoReanudar { Clave = Clave(organismoId, ipHash), OcurrioEn = reloj.GetUtcNow() });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task LiberarAsync(Guid organismoId, byte[]? ipHash, CancellationToken ct)
    {
        var clave = Clave(organismoId, ipHash);
        await db.Set<IntentoReanudar>().Where(i => i.Clave == clave).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    internal static byte[] Clave(Guid organismoId, byte[]? ipHash) =>
        SHA256.HashData([.. Encoding.UTF8.GetBytes("cierre-codigo:" + organismoId.ToString("N") + ":"), .. (ipHash ?? [])]);
}
