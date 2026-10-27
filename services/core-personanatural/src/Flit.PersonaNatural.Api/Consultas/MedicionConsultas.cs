using System.Data.Common;
using Flit.PersonaNatural.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.PersonaNatural.Api.Consultas;

/// <summary>Consultas atendidas de un tipo en un día (UTC, el mismo día que usan los topes de <c>cuota_consulta</c>).</summary>
public sealed record ConsumoDia(DateOnly Dia, string Tipo, int Total);

/// <summary>
/// Medición de consumo del portal por tipo de consulta y por día (HU #14041, AC5). Reutiliza <c>cuota_consulta</c> con la
/// clave <c>med:&lt;tipo&gt;</c>: persiste los reinicios, se comparte entre réplicas y el incremento es atómico, y no hace
/// falta otra tabla. Cuenta las consultas que LLEGARON a core-consultas (incluidas las que el proveedor no pudo responder);
/// las rechazadas antes (captcha, formato, topes, sin identidad) no consumen nada y no cuentan. Es el conteo del portal: el
/// consumo contra los proveedores, por empresa y producto, lo lleva core-consultas.
/// <para>
/// Solo es un servicio: la pantalla de administración es del Feature J y se publica con autenticación. El gateway enruta
/// <c>/api/v1/persona-natural/*</c> al público, así que NO se expone aquí ningún endpoint de lectura.
/// </para>
/// </summary>
public sealed partial class MedicionConsultasService(PersonaNaturalDb db, TimeProvider reloj, ILogger<MedicionConsultasService> logger)
{
    public const string TipoVehiculo = "vehiculo";
    public const string TipoPersona = "persona";
    public const string TipoVehiculoDetalle = "vehiculo_detalle";

    public static readonly IReadOnlyList<string> Tipos = [TipoVehiculo, TipoPersona, TipoVehiculoDetalle];

    internal const string PrefijoClave = "med:";

    /// <summary>
    /// Suma una consulta del <paramref name="tipo"/> al día de hoy. Si la base falla no se propaga: perder un conteo no puede
    /// tumbar una consulta que ya se hizo (y pagó).
    /// </summary>
    public async Task RegistrarAsync(string tipo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        var dia = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var sql = $"""
            INSERT INTO {PersonaNaturalDb.Schema}.cuota_consulta (clave, dia, usadas) VALUES (@clave, @dia, 1)
            ON CONFLICT (clave, dia) DO UPDATE SET usadas = cuota_consulta.usadas + 1
            """;
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("clave", PrefijoClave + tipo), new NpgsqlParameter("dia", dia)], ct).ConfigureAwait(false);
        }
        catch (DbException)
        {
            MedicionPerdida(logger, tipo);
        }
    }

    /// <summary>Conteo por día y tipo entre dos fechas (inclusive), de la más antigua a la más reciente. Los días sin consultas no aparecen.</summary>
    public async Task<IReadOnlyList<ConsumoDia>> ConsultarAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var filas = await db.Set<CuotaConsulta>().AsNoTracking()
            .Where(c => c.Clave.StartsWith(PrefijoClave) && c.Dia >= desde && c.Dia <= hasta)
            .OrderBy(c => c.Dia).ThenBy(c => c.Clave)
            .ToListAsync(ct).ConfigureAwait(false);
        return filas.Select(c => new ConsumoDia(c.Dia, c.Clave[PrefijoClave.Length..], c.Usadas)).ToList();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo registrar la medición de una consulta de tipo {Tipo}.")]
    private static partial void MedicionPerdida(ILogger logger, string tipo);
}
