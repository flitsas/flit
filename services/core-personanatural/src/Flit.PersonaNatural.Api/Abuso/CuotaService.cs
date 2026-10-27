using System.Globalization;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Sesiones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.PersonaNatural.Api.Abuso;

public enum ResultadoCuota
{
    Permitida,
    CedulaExcedida,
    TopeGlobal,
}

/// <summary>
/// Contadores de consultas en la tabla <c>cuota_consulta</c> (persisten los reinicios y se comparten entre réplicas).
/// El incremento es atómico (<c>INSERT … ON CONFLICT</c>), así dos peticiones simultáneas no se saltan el tope.
/// </summary>
public sealed class CuotaService(PersonaNaturalDb db, PiiHasher hasher, TimeProvider reloj, IOptions<AbusoOptions> options)
{
    public const string ClaveGlobal = "global:consultas";
    public const string AccionCedulaExcedida = "limite_cedula_excedido";
    public const string AccionTopeGlobal = "tope_global_alcanzado";

    private readonly AbusoOptions _opciones = options.Value;

    /// <summary>
    /// Cuenta una consulta nueva y dice si puede ejecutarse. Primero el tope por cédula (la clave lleva la huella, nunca el
    /// número), luego el global; una consulta rechazada por cédula no gasta del tope global. Los rechazos quedan auditados.
    /// </summary>
    public async Task<ResultadoCuota> ReservarConsultaAsync(string cedula, byte[]? ipHash, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var dia = DateOnly.FromDateTime(ahora.UtcDateTime);

        var claveCedula = $"ced:{Convert.ToHexString(hasher.HashCedula(cedula))}:{ahora.UtcDateTime.ToString("yyyyMMddHH", CultureInfo.InvariantCulture)}";
        if (await ConsumirAsync(claveCedula, dia, ct).ConfigureAwait(false) > _opciones.LimiteCedulaPorHora)
        {
            await AuditarAsync(AccionCedulaExcedida, ipHash, ahora, ct).ConfigureAwait(false);
            return ResultadoCuota.CedulaExcedida;
        }

        var global = await ConsumirAsync(ClaveGlobal, dia, ct).ConfigureAwait(false);
        if (global > _opciones.TopeGlobalDiario)
        {
            if (global == _opciones.TopeGlobalDiario + 1)
                await AuditarAsync(AccionTopeGlobal, ipHash, ahora, ct).ConfigureAwait(false);
            return ResultadoCuota.TopeGlobal;
        }

        return ResultadoCuota.Permitida;
    }

    private async Task<int> ConsumirAsync(string clave, DateOnly dia, CancellationToken ct)
    {
        var sql = $"""
            INSERT INTO {PersonaNaturalDb.Schema}.cuota_consulta (clave, dia, usadas) VALUES (@clave, @dia, 1)
            ON CONFLICT (clave, dia) DO UPDATE SET usadas = cuota_consulta.usadas + 1
            RETURNING usadas AS "Value"
            """;
        // El INSERT … RETURNING no se puede componer con otro operador de LINQ: se materializa la fila tal cual.
        var filas = await db.Database.SqlQueryRaw<int>(sql, new NpgsqlParameter("clave", clave), new NpgsqlParameter("dia", dia))
            .ToListAsync(ct).ConfigureAwait(false);
        return filas[0];
    }

    private async Task AuditarAsync(string accion, byte[]? ipHash, DateTimeOffset ahora, CancellationToken ct)
    {
        db.Add(new AccesoPiiLog { Actor = "sistema", Accion = accion, IpHash = ipHash, OcurrioEn = ahora });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
