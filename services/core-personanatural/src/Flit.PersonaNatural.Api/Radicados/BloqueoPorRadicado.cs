using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Radicados;

/// <summary>
/// ÚNICO punto de política del bloqueo por radicado tras fallos seguidos (HU #14035/#14042): «reanudar» y «consultar radicado»
/// gastan el mismo cupo, de modo que quien adivina cédulas por una ruta no gana intentos por la otra. La clave es la huella del
/// radicado, exista o no, para no revelar cuáles existen.
///
/// Decisión contra la negación de servicio dirigida: bloquear a duras penas por radicado dejaba a cualquiera «apagar» el
/// radicado de otro con cinco intentos desde cualquier IP. Por eso cada intento cuenta dos veces: por (radicado, IP) con el
/// cupo normal (<see cref="SesionOptions.MaxIntentosFallidos"/>), que solo bloquea a quien insiste, y por radicado a secas
/// con un cupo mayor (<see cref="SesionOptions.MaxIntentosPorRadicado"/>), reservado para el patrón de un ataque
/// distribuido. El dueño legítimo, desde su IP, no queda bloqueado por el abuso ajeno hasta ese cupo mayor. Ambos contadores
/// viven en la tabla de intentos (el segundo con una clave derivada de radicado+IP: sin cambiar el esquema).
///
/// La comprobación y el registro son atómicos: se reserva el intento ANTES de validar, bajo un cerrojo consultivo de
/// Postgres por radicado; N peticiones paralelas se serializan y solo pasan las que caben en el cupo. Un acierto libera los
/// contadores.
/// </summary>
public sealed class BloqueoPorRadicado(PersonaNaturalDb db, TimeProvider reloj, IOptions<SesionOptions> options)
{
    private readonly SesionOptions _opciones = options.Value;

    /// <summary>
    /// Reserva un intento para (<paramref name="clave"/>, <paramref name="ipHash"/>). Devuelve cuánto falta para que se libere si
    /// el radicado (o la IP sobre él) está bloqueado —sin gastar cupo—, o <c>null</c> si el intento quedó reservado y puede validarse.
    /// </summary>
    public async Task<TimeSpan?> ReservarIntentoAsync(byte[] clave, byte[]? ipHash, CancellationToken ct)
    {
        var claveIp = PiiHasher.ClaveRadicadoIp(clave, ipHash);
        var cerrojo = BitConverter.ToInt64(clave, 0);

        // La transacción propia exige correr dentro de la estrategia de ejecución (reintentos de Npgsql).
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct2 =>
        {
            // Si la estrategia reintenta, el intento agregado en la vuelta anterior no debe quedar rastreado dos veces.
            foreach (var pendiente in db.ChangeTracker.Entries<IntentoReanudar>().Where(e => e.State == EntityState.Added).ToList())
                pendiente.State = EntityState.Detached;

            await using var tx = await db.Database.BeginTransactionAsync(ct2).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({cerrojo})", ct2).ConfigureAwait(false);

            var ahora = reloj.GetUtcNow();
            var ventana = TimeSpan.FromMinutes(_opciones.VentanaIntentosMinutos);
            var porIp = await EsperaAsync(claveIp, _opciones.MaxIntentosFallidos, ahora, ventana, ct2).ConfigureAwait(false);
            var porRadicado = await EsperaAsync(clave, Math.Max(_opciones.MaxIntentosPorRadicado, _opciones.MaxIntentosFallidos), ahora, ventana, ct2).ConfigureAwait(false);
            if (porIp is not null || porRadicado is not null)
                return (TimeSpan?)(porIp is { } a && (porRadicado is not { } b || a >= b) ? a : porRadicado);

            db.AddRange(new IntentoReanudar { Clave = clave, OcurrioEn = ahora }, new IntentoReanudar { Clave = claveIp, OcurrioEn = ahora });
            await db.SaveChangesAsync(ct2).ConfigureAwait(false);
            await tx.CommitAsync(ct2).ConfigureAwait(false);
            return null;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Un acierto borra los intentos del radicado y los de esa IP sobre él (los de otras IP vencen con la ventana).</summary>
    public async Task LiberarAsync(byte[] clave, byte[]? ipHash, CancellationToken ct)
    {
        var claveIp = PiiHasher.ClaveRadicadoIp(clave, ipHash);
        await db.Set<IntentoReanudar>().Where(i => i.Clave == clave || i.Clave == claveIp).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Cuánto falta para que baje del cupo, o <c>null</c> si todavía cabe un intento.</summary>
    private async Task<TimeSpan?> EsperaAsync(byte[] clave, int cupo, DateTimeOffset ahora, TimeSpan ventana, CancellationToken ct)
    {
        var desde = ahora - ventana;
        var intentos = await db.Set<IntentoReanudar>().AsNoTracking().Where(i => i.Clave == clave && i.OcurrioEn > desde)
            .OrderBy(i => i.OcurrioEn).Select(i => i.OcurrioEn).ToListAsync(ct).ConfigureAwait(false);
        if (intentos.Count < cupo)
            return null;

        var libera = intentos[intentos.Count - cupo] + ventana;
        return libera > ahora ? libera - ahora : TimeSpan.FromSeconds(1);
    }
}
