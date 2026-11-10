using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Flit.PersonaNatural.Api.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Tope de búsquedas sin resultado (ADR-0082, enumeración de IDs <c>PN-AAAA-NNNNNN</c>) por usuario (10 en 5 minutos por defecto) Y por
/// IP dentro del organismo (huella HMAC). El conteo sale de <c>cashier_event</c>, no de memoria, así que vale con varias réplicas y
/// sobrevive a un reinicio.
/// <para>
/// Sin carrera leer-contar-escribir: <see cref="ReservarAsync"/> toma un <c>pg_advisory_xact_lock</c> por usuario (y por IP), cuenta y,
/// si hay cupo, deja AHÍ MISMO una fila <c>busqueda_fallida</c>/<c>pendiente</c> que ocupa un cupo. Cuando se conoce el resultado la fila se
/// convierte en la búsqueda real (<see cref="ResolverAsync"/>) o se retira si la operación no era una búsqueda fallida
/// (<see cref="LiberarAsync"/>). Así N peticiones paralelas no superan el tope, y si el proceso muere a mitad queda el cupo ocupado (falla cerrado).
/// </para>
/// </summary>
public sealed class CajaTopeBusquedas(PersonaNaturalDb db, IOptions<CajaOptions> opciones, TimeProvider reloj, PiiHasher hasher)
{
    public const string ResultadoPendiente = "pendiente";
    public const string CodigoDemasiadosIntentos = "demasiados_intentos";

    /// <summary>
    /// Ocupa un cupo de búsqueda sin resultado para el cajero (y su IP). Si ya llegó al tope por usuario o por IP lanza 429
    /// <c>demasiados_intentos</c> con <c>retryAfterSegundos</c>. Devuelve el id de la reserva.
    /// </summary>
    public async Task<Guid> ReservarAsync(Cajero cajero, string? ip, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cajero);
        var o = opciones.Value;
        var ventana = TimeSpan.FromMinutes(Math.Max(1, o.BusquedasFallidasVentanaMin));
        var ahora = reloj.GetUtcNow();
        var desde = ahora - ventana;
        var maxUsuario = Math.Max(1, o.BusquedasFallidasMax);
        var maxIp = Math.Max(maxUsuario, o.BusquedasFallidasMaxPorIp);
        var huella = string.IsNullOrEmpty(ip) ? null : Convert.ToHexStringLower(hasher.HashIp(ip));

        return await Transaccion.EjecutarAsync(db, async ct2 =>
        {
            // Siempre en el mismo orden (usuario y luego IP): dos peticiones nunca se esperan en círculo.
            var claveUsuario = $"caja-tope-u:{cajero.UsuarioId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({claveUsuario}, 0))", ct2).ConfigureAwait(false);
            if (huella is not null)
            {
                var claveIp = $"caja-tope-ip:{cajero.OrganismoId:N}:{huella}";
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({claveIp}, 0))", ct2).ConfigureAwait(false);
            }

            var porUsuario = await db.Set<EventoCaja>().AsNoTracking()
                .Where(e => e.UsuarioId == cajero.UsuarioId && e.Accion == EventoCaja.AccionBusquedaFallida && e.CreadoEn > desde)
                .OrderBy(e => e.CreadoEn).Select(e => e.CreadoEn).ToListAsync(ct2).ConfigureAwait(false);
            var espera = EsperaPara(porUsuario, maxUsuario, ventana, ahora);

            if (huella is not null)
            {
                // El índice (organismo, fecha) acota la lectura; la IP se compara por huella.
                var porIp = await db.Set<EventoCaja>().AsNoTracking()
                    .Where(e => e.OrganismoId == cajero.OrganismoId && e.CreadoEn > desde && e.Accion == EventoCaja.AccionBusquedaFallida && e.IpHuella == huella)
                    .OrderBy(e => e.CreadoEn).Select(e => e.CreadoEn).ToListAsync(ct2).ConfigureAwait(false);
                if (EsperaPara(porIp, maxIp, ventana, ahora) is { } esperaIp && (espera is null || esperaIp > espera))
                    espera = esperaIp;
            }

            if (espera is { } debe)
            {
                throw new ReglaDeNegocioException(
                    StatusCodes.Status429TooManyRequests, CodigoDemasiadosIntentos, "Demasiados intentos", "Demasiadas búsquedas sin resultado. Espere unos minutos.",
                    new Dictionary<string, object?> { ["retryAfterSegundos"] = (int)Math.Ceiling(debe.TotalSeconds) });
            }

            var reserva = CajaAuditoria.Nuevo(cajero.OrganismoId, cajero.UsuarioId, EventoCaja.AccionBusquedaFallida, null, ResultadoPendiente, ip, ahora, hasher);
            db.Add(reserva);
            await db.SaveChangesAsync(ct2).ConfigureAwait(false);
            return reserva.Id;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Convierte la reserva en el resultado real: <c>busqueda_fallida</c> (sigue contando) o <c>busqueda_ok</c> (deja de contar).</summary>
    public async Task ResolverAsync(Guid reserva, string accion, string? codigo, string resultado, CancellationToken ct)
    {
        var cod = codigo is { Length: > 20 } ? codigo[..20] : codigo;
        var res = resultado.Length > 30 ? resultado[..30] : resultado;
        db.ChangeTracker.Clear();
        await db.Set<EventoCaja>().Where(e => e.Id == reserva)
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.Accion, accion).SetProperty(e => e.Codigo, cod).SetProperty(e => e.Resultado, res), ct).ConfigureAwait(false);
    }

    /// <summary>Retira la reserva: lo que se hizo no fue una búsqueda sin resultado (o falló por causas ajenas al usuario).</summary>
    public async Task LiberarAsync(Guid reserva, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await db.Set<EventoCaja>().Where(e => e.Id == reserva && e.Resultado == ResultadoPendiente).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Cuánto falta para volver a tener cupo; <c>null</c> si hay cupo.</summary>
    private static TimeSpan? EsperaPara(List<DateTimeOffset> fallidas, int max, TimeSpan ventana, DateTimeOffset ahora)
    {
        if (fallidas.Count < max)
            return null;

        // Se libera cuando la fallida que deja el conteo por debajo del tope sale de la ventana.
        var libera = fallidas[fallidas.Count - max] + ventana;
        var espera = libera - ahora;
        return espera < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : espera;
    }
}
