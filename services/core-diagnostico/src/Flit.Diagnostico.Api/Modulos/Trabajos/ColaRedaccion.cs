using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Una redacción tomada de la cola: lo justo para fijar la empresa. No trae el texto.</summary>
/// <param name="Id">Id del dictamen.</param>
/// <param name="TenantId">Empresa del dictamen; el redactor la fija en su contexto antes de leer o escribir nada más.</param>
/// <param name="Intentos">Intentos de redacción contando esta toma.</param>
/// <param name="LeaseHasta">Vencimiento del lease, tal como quedó en la base (identifica esta toma).</param>
public sealed record RedaccionTomada(Guid Id, Guid TenantId, int Intentos, DateTimeOffset LeaseHasta);

/// <summary>
/// Toma de la cola de redacción (corte 6, ADR-0072): la tabla <c>diagnostico.dictamenes</c> es la cola de TODAS las empresas
/// (<c>ix_redaccion_pendiente</c>). En una transacción corta, <c>SELECT … FOR UPDATE SKIP LOCKED</c> de la fila tomable más
/// antigua (PENDIENTE vigente, con fecha de toma ya cumplida y lease null o vencido) y le pone
/// <c>redaccion_lease_hasta = ahora + lease</c> y un intento más. Sigue en PENDIENTE: el CHECK no admite otro estado. Dos
/// trabajadores nunca toman la misma. El reloj es el inyectado (<see cref="TimeProvider"/>), no el de la base.
/// <para>
/// Es, con <see cref="ColaConsultas"/> y <see cref="ColaEvidencias"/>, la única lectura y escritura del servicio que cruza
/// empresas sin el filtro de EF, y por eso es SQL crudo parametrizado: solo toca columnas de la cola y lo que devuelve son el
/// id, la empresa, los intentos y el lease. <c>TomaEntreEmpresasSoloEnColaTests</c> falla si aparece SQL crudo en otro archivo.
/// </para>
/// </summary>
internal sealed class ColaRedaccion(DiagnosticoDb db, TimeProvider reloj)
{
    public const string Pendiente = "PENDIENTE";

    public const string Completada = "COMPLETADA";

    // Los literales (no entrada de usuario) coinciden con el predicado de ix_redaccion_pendiente para que el planificador lo use.
    private const string SqlTomar = """
        WITH candidata AS (
            SELECT id
            FROM diagnostico.dictamenes
            WHERE estado_redaccion = 'PENDIENTE' AND vigente
              AND redaccion_disponible_desde IS NOT NULL AND redaccion_disponible_desde <= @ahora
              AND (redaccion_lease_hasta IS NULL OR redaccion_lease_hasta <= @ahora)
            ORDER BY redaccion_disponible_desde, id
            LIMIT 1
            FOR UPDATE SKIP LOCKED)
        UPDATE diagnostico.dictamenes AS d
        SET redaccion_lease_hasta = @lease, redaccion_intentos = d.redaccion_intentos + 1
        FROM candidata
        WHERE d.id = candidata.id
        RETURNING d.id, d.tenant_id, d.redaccion_intentos, d.redaccion_lease_hasta
        """;

    /// <summary>
    /// true si la fila entraría en la toma: PENDIENTE, vigente, con fecha ya cumplida y lease null o vencido. No abre Postgres.
    /// </summary>
    public static bool EsTomable(string estadoRedaccion, bool vigente, DateTimeOffset? disponibleDesde, DateTimeOffset? leaseHasta, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(estadoRedaccion);
        if (estadoRedaccion != Pendiente || !vigente || disponibleDesde is null || disponibleDesde > ahora)
            return false;
        return leaseHasta is null || leaseHasta <= ahora;
    }

    /// <summary>Toma la redacción más antigua de la cola con un lease de <paramref name="lease"/>; null si no hay ninguna tomable.</summary>
    public async Task<RedaccionTomada?> TomarAsync(TimeSpan lease, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var ahora = reloj.GetUtcNow();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
            RedaccionTomada? tomada = null;
            await using (var tomar = new NpgsqlCommand(SqlTomar, conexion, (NpgsqlTransaction)transaccion.GetDbTransaction()))
            {
                tomar.Parameters.AddWithValue("ahora", ahora);
                tomar.Parameters.AddWithValue("lease", ahora + lease);
                await using var lector = await tomar.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    tomada = new RedaccionTomada(
                        lector.GetGuid(0), lector.GetGuid(1), lector.GetInt32(2), lector.GetFieldValue<DateTimeOffset>(3));
                }
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return tomada;
        }).ConfigureAwait(false);
    }
}
