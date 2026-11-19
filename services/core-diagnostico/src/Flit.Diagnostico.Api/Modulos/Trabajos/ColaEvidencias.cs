using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>Un ZIP tomado de la cola: lo justo para fijar la empresa y generarlo.</summary>
/// <param name="Id">Id de la evidencia ZIP.</param>
/// <param name="TenantId">Empresa de la evidencia; el empaquetador la fija en su contexto antes de leer o escribir nada.</param>
/// <param name="DiagnosticoId">Lote del ZIP.</param>
/// <param name="Intentos">Intentos de generación contando esta toma.</param>
/// <param name="LeaseHasta">Vencimiento del lease, tal como quedó en la base (identifica esta toma).</param>
public sealed record EvidenciaTomada(Guid Id, Guid TenantId, Guid DiagnosticoId, int Intentos, DateTimeOffset LeaseHasta);

/// <summary>
/// Toma de la cola del ZIP del lote (HU #13850, decisión Q2; patrón de <see cref="ColaConsultas"/>): la tabla
/// <c>diagnostico.evidencias</c> es la cola de TODAS las empresas (<c>ix_evidencias_cola</c>). En una transacción corta,
/// <c>SELECT … FOR UPDATE SKIP LOCKED</c> del ZIP tomable más antiguo (PENDIENTE sin espera de reintento o con la espera cumplida,
/// o GENERANDO con el lease vencido: el proceso que lo tenía murió) y lo pasa a GENERANDO con <c>lease_hasta = ahora + lease</c> y un
/// intento más. Dos trabajadores nunca toman el mismo: la fila que otro tiene bloqueada se salta sin esperar. El reloj es el
/// inyectado (<see cref="TimeProvider"/>), no el de la base.
/// <para>
/// Es, con <see cref="ColaConsultas"/>, la única lectura y escritura del servicio que cruza empresas sin el filtro de EF, y por eso
/// es SQL crudo parametrizado: solo toca columnas de la cola (estado, lease, intentos) de filas ZIP y lo que devuelve son ids, la
/// empresa, los intentos y el lease; nunca datos del lote. <c>TomaEntreEmpresasSoloEnColaTests</c> falla si aparece SQL crudo en
/// otro archivo. Todo lo demás del trabajo lo hace <see cref="EmpaquetadorZip"/> con la empresa de la fila fijada en su contexto.
/// </para>
/// </summary>
internal sealed class ColaEvidencias(DiagnosticoDb db, TimeProvider reloj)
{
    private static readonly string Zip = CodigosDeEnum.Codigo(TipoEvidencia.Zip);
    private static readonly string Pendiente = CodigosDeEnum.Codigo(EstadoEvidencia.Pendiente);
    private static readonly string Generando = CodigosDeEnum.Codigo(EstadoEvidencia.Generando);

    // Los códigos van como literales (constantes de los enums, no entrada de usuario) para que el planificador use el índice
    // parcial ix_evidencias_cola, cuyo predicado es tipo = 'ZIP' AND estado IN ('PENDIENTE', 'GENERANDO').
    private static readonly string SqlTomar = $"""
        WITH candidata AS (
            SELECT id
            FROM diagnostico.evidencias
            WHERE tipo = '{Zip}' AND estado IN ('{Pendiente}', '{Generando}')
              AND ((estado = '{Pendiente}' AND (lease_hasta IS NULL OR lease_hasta <= @ahora))
                OR (estado = '{Generando}' AND lease_hasta < @ahora))
            ORDER BY creado_en, id
            LIMIT 1
            FOR UPDATE SKIP LOCKED)
        UPDATE diagnostico.evidencias AS e
        SET estado = '{Generando}', lease_hasta = @lease, intentos = e.intentos + 1
        FROM candidata
        WHERE e.id = candidata.id
        RETURNING e.id, e.tenant_id, e.diagnostico_id, e.intentos, e.lease_hasta
        """;

    /// <summary>Toma el ZIP más antiguo de la cola con un lease de <paramref name="lease"/>; null si no hay ninguno tomable.</summary>
    public async Task<EvidenciaTomada?> TomarAsync(TimeSpan lease, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lease, TimeSpan.Zero);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var ahora = reloj.GetUtcNow();
            await using var transaccion = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
            EvidenciaTomada? tomada = null;
            await using (var tomar = new NpgsqlCommand(SqlTomar, conexion, (NpgsqlTransaction)transaccion.GetDbTransaction()))
            {
                tomar.Parameters.AddWithValue("ahora", ahora);
                tomar.Parameters.AddWithValue("lease", ahora + lease);
                await using var lector = await tomar.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    tomada = new EvidenciaTomada(
                        lector.GetGuid(0), lector.GetGuid(1), lector.GetGuid(2), lector.GetInt32(3), lector.GetFieldValue<DateTimeOffset>(4));
                }
            }

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return tomada;
        }).ConfigureAwait(false);
    }
}
