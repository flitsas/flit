using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Una celda tomada de la cola: lo justo para fijar la empresa y despachar al ejecutor de su fuente.</summary>
/// <param name="Id">Id de la celda.</param>
/// <param name="TenantId">Empresa de la celda; el procesador la fija antes de leer o escribir.</param>
/// <param name="DiagnosticoId">Lote.</param>
/// <param name="VehiculoId">Vehículo.</param>
/// <param name="Fuente">Fuente a consultar.</param>
/// <param name="LeaseHasta">Vencimiento del lease, tal como quedó en la base (identifica esta toma).</param>
/// <param name="IntentosAuto">Reintentos automáticos ya programados para la celda (HU #13737, D-12).</param>
public sealed record CeldaTomada(Guid Id, Guid TenantId, Guid DiagnosticoId, Guid VehiculoId, Fuente Fuente, DateTimeOffset LeaseHasta, int IntentosAuto = 0);

/// <summary>
/// Toma de la cola durable (HU #13727, ADR a A2): la tabla <c>diagnostico.consultas</c> es la cola de TODAS las empresas
/// (<c>ix_cola</c>). En una transacción corta: <c>SELECT … FOR UPDATE SKIP LOCKED</c> de las celdas tomables (PENDIENTE con
/// <c>disponible_desde</c> cumplido, o EN_CURSO con el lease vencido: el proceso que la tenía murió), descarta las que pasarían
/// de <c>vehiculosPorEmpresa</c> vehículos en vuelo de su empresa (plan §10) y pasa el resto a EN_CURSO con
/// <c>lease_hasta = ahora + lease</c>. Dos procesadores nunca toman la misma fila: la que otro tiene bloqueada se salta sin
/// esperar. El reloj es el inyectado (<see cref="TimeProvider"/>), no el de la base.
/// <para>
/// Es la ÚNICA lectura y escritura del servicio que cruza empresas sin el filtro de EF, y por eso es SQL crudo parametrizado;
/// solo toca columnas de la cola (estado, lease, actualizado_en), nunca datos de negocio, y lo que lee no sale de aquí salvo los
/// ids y <c>intentos_auto</c> (la política de reintentos, HU #13737). <c>TomaEntreEmpresasSoloEnColaTests</c> falla si aparece SQL crudo en otro archivo. Lo demás del trabajo se hace con
/// la empresa de cada celda fijada en el contexto.
/// </para>
/// </summary>
internal sealed class ColaConsultas(DiagnosticoDb db, TimeProvider reloj)
{
    private static readonly string Pendiente = CodigosDeEnum.Codigo(EstadoCelda.Pendiente);
    private static readonly string EnCurso = CodigosDeEnum.Codigo(EstadoCelda.EnCurso);

    // Los códigos de estado van como literales (constantes del enum, no entrada de usuario) para que el planificador pueda
    // usar el índice parcial ix_cola, cuyo predicado es estado IN ('PENDIENTE', 'EN_CURSO').
    private static readonly string SqlCandidatas = $"""
        SELECT id, tenant_id, vehiculo_id
        FROM diagnostico.consultas
        WHERE estado IN ('{Pendiente}', '{EnCurso}')
          AND fuente = ANY(@fuentes)
          AND ((estado = '{Pendiente}' AND disponible_desde <= @ahora) OR (estado = '{EnCurso}' AND lease_hasta < @ahora))
        ORDER BY disponible_desde, id
        LIMIT @candidatas
        FOR UPDATE SKIP LOCKED
        """;

    private static readonly string SqlEnVuelo = $"""
        SELECT DISTINCT tenant_id, vehiculo_id
        FROM diagnostico.consultas
        WHERE estado = '{EnCurso}' AND lease_hasta >= @ahora AND tenant_id = ANY(@empresas)
        """;

    private static readonly string SqlTomar = $"""
        UPDATE diagnostico.consultas
        SET estado = '{EnCurso}', lease_hasta = @lease, actualizado_en = @ahora
        WHERE id = ANY(@ids)
        RETURNING id, tenant_id, diagnostico_id, vehiculo_id, fuente, lease_hasta, intentos_auto
        """;

    /// <summary>Toma hasta <paramref name="maximo"/> celdas de <paramref name="fuentes"/> y les pone el lease.</summary>
    public async Task<IReadOnlyList<CeldaTomada>> TomarAsync(
        int maximo, TimeSpan lease, int vehiculosPorEmpresa, IReadOnlyCollection<Fuente> fuentes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(vehiculosPorEmpresa, 1);
        ArgumentNullException.ThrowIfNull(fuentes);
        if (fuentes.Count == 0)
            return [];

        var codigos = fuentes.Distinct().Select(CodigosDeEnum.Codigo).ToArray();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var ahora = reloj.GetUtcNow();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
            var tx = (NpgsqlTransaction)transaccion.GetDbTransaction();

            var candidatas = new List<(Guid Id, Guid Empresa, Guid Vehiculo)>();
            await using (var consulta = new NpgsqlCommand(SqlCandidatas, conexion, tx))
            {
                consulta.Parameters.AddWithValue("fuentes", codigos);
                consulta.Parameters.AddWithValue("ahora", ahora);
                // Más candidatas que el máximo: las que exceden el cupo de su empresa se descartan sin dejar huecos.
                consulta.Parameters.AddWithValue("candidatas", maximo * 4);
                await using var lector = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                    candidatas.Add((lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2)));
            }

            var elegidas = await ElegirAsync(conexion, tx, candidatas, maximo, vehiculosPorEmpresa, ahora, ct).ConfigureAwait(false);
            var tomadas = new List<CeldaTomada>(elegidas.Count);
            if (elegidas.Count > 0)
            {
                await using var tomar = new NpgsqlCommand(SqlTomar, conexion, tx);
                tomar.Parameters.AddWithValue("ids", elegidas.ToArray());
                tomar.Parameters.AddWithValue("lease", ahora + lease);
                tomar.Parameters.AddWithValue("ahora", ahora);
                await using var lector = await tomar.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    CodigosDeEnum.TryLeer<Fuente>(lector.GetString(4), out var fuente);
                    tomadas.Add(new CeldaTomada(
                        lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetGuid(3), fuente, lector.GetFieldValue<DateTimeOffset>(5),
                        lector.GetInt32(6)));
                }
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return (IReadOnlyList<CeldaTomada>)tomadas;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Las candidatas que caben, en orden de la cola: una celda de un vehículo que ya está en vuelo no suma cupo; un vehículo
    /// nuevo de una empresa que ya tiene <paramref name="vehiculosPorEmpresa"/> en vuelo espera a la siguiente vuelta.
    /// </summary>
    private static async Task<List<Guid>> ElegirAsync(
        NpgsqlConnection conexion, NpgsqlTransaction tx, List<(Guid Id, Guid Empresa, Guid Vehiculo)> candidatas, int maximo, int vehiculosPorEmpresa,
        DateTimeOffset ahora, CancellationToken ct)
    {
        if (candidatas.Count == 0)
            return [];

        var enVuelo = new Dictionary<Guid, HashSet<Guid>>();
        await using (var consulta = new NpgsqlCommand(SqlEnVuelo, conexion, tx))
        {
            consulta.Parameters.AddWithValue("ahora", ahora);
            consulta.Parameters.AddWithValue("empresas", candidatas.Select(c => c.Empresa).Distinct().ToArray());
            await using var lector = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await lector.ReadAsync(ct).ConfigureAwait(false))
                Vehiculos(enVuelo, lector.GetGuid(0)).Add(lector.GetGuid(1));
        }

        var elegidas = new List<Guid>();
        foreach (var (id, empresa, vehiculo) in candidatas)
        {
            if (elegidas.Count == maximo)
                break;
            var vehiculos = Vehiculos(enVuelo, empresa);
            if (!vehiculos.Contains(vehiculo) && vehiculos.Count >= vehiculosPorEmpresa)
                continue;
            vehiculos.Add(vehiculo);
            elegidas.Add(id);
        }

        return elegidas;
    }

    private static HashSet<Guid> Vehiculos(Dictionary<Guid, HashSet<Guid>> enVuelo, Guid empresa)
    {
        if (!enVuelo.TryGetValue(empresa, out var vehiculos))
            enVuelo[empresa] = vehiculos = [];
        return vehiculos;
    }
}
