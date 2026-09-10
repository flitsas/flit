using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Recorrido de un pre-trámite con sus tiempos por etapa (HU #11816).
/// </summary>
/// <remarks>
/// <para>
/// Las marcas salen del propio master, que es la fuente que siempre está: <c>created_at</c>,
/// <c>business_date_validation</c> y <c>external_date_validation</c>. Son exactamente las que FLIT
/// 1.0 cronometraba (<c>fecharegistrotransaccion</c>, <c>fechavalidacionnegocio</c>,
/// <c>fechaidentificacionfuentes</c>).
/// </para>
/// <para>
/// La cuarta, <c>fechacreaciontramite</c>, no tiene columna propia en v2. Se toma del historial de
/// etapas (<c>ict.external_integration_process_status</c>, la tabla que escriben los stored
/// procedures y el equivalente directo de la de v1) y solo si ahí no hay nada se cae a
/// <c>updated_at</c>. Esa caída es una APROXIMACIÓN: <c>updated_at</c> se mueve con cualquier cambio
/// posterior, así que sobreestima. Se prefiere sobreestimar a no informar el tiempo, pero conviene
/// saberlo al leer la cifra.
/// </para>
/// </remarks>
public sealed class DbRecorridoTramiteRepository(IctDbContext db) : IRecorridoTramiteQuery
{
    /// <summary>Etapas del catálogo <c>ict.external_integration_parameter_process_status</c>.</summary>
    private const short EtapaConNovedades = 4;
    private const short EtapaBorrador = 5;
    private const short EtapaAnulado = 6;

    public async Task<RecorridoTramite?> ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT
                    m.id, m.transaction_number, m.manager_id_transaction, m.plate, m.vin,
                    pt.name AS tipo_tramite, ot.name AS operacion,
                    m.tenant_id, t.legal_name AS compania,
                    {DbTrazabilidadBandejaRepository.EstadoSql} AS estado,
                    m.created_at, m.business_date_validation, m.external_date_validation,
                    COALESCE(borrador.marca, CASE WHEN m.procedure_instance_id IS NOT NULL THEN m.updated_at END) AS borrador_creado,
                    anulado.marca AS anulado,
                    COALESCE(novedad.mensaje,
                             NULLIF(m.external_comments_validation, ''),
                             NULLIF(m.business_comments_validation, '')) AS mensaje_novedad,
                    m.procedure_instance_id, m.traffic_secretary_code, m.runt_transit_office_name,
                    now() AS ahora
                FROM ict.external_integration_master m
                LEFT JOIN ict.external_integration_procedure_type pt ON pt.id = m.transaction_type
                LEFT JOIN ict.external_integration_operation_type ot ON ot.id = m.transaction_operation
                LEFT JOIN identity.tenants t ON t.id = m.tenant_id
                -- La PRIMERA vez que el trámite alcanzó cada desenlace. Un reproceso puede repetir la
                -- etapa, y lo que se cronometra es cuándo llegó, no cuándo se repitió.
                -- Sin filtro de borrado lógico: a diferencia del master, el historial de etapas de v2 no
                -- tiene deleted_at. Es un registro de hechos y no se borra.
                LEFT JOIN LATERAL (
                    SELECT MIN(s.status_process_registrationdate) AS marca
                    FROM ict.external_integration_process_status s
                    WHERE s.id_eimas = m.id AND s.id_parprosta = {EtapaBorrador}
                ) borrador ON TRUE
                LEFT JOIN LATERAL (
                    SELECT MIN(s.status_process_registrationdate) AS marca
                    FROM ict.external_integration_process_status s
                    WHERE s.id_eimas = m.id AND s.id_parprosta = {EtapaAnulado}
                ) anulado ON TRUE
                -- El mensaje de novedad más reciente: si el cliente reprocesó y volvió a fallar, lo que
                -- importa es por qué está fallando ahora, no la primera vez.
                LEFT JOIN LATERAL (
                    SELECT s.message_validation AS mensaje
                    FROM ict.external_integration_process_status s
                    WHERE s.id_eimas = m.id AND s.id_parprosta = {EtapaConNovedades}
                          AND NULLIF(s.message_validation, '') IS NOT NULL
                    ORDER BY s.status_process_registrationdate DESC
                    LIMIT 1
                ) novedad ON TRUE
                WHERE m.transaction_number = @numero
                  AND m.deleted_at IS NULL
                  AND (@tenant::uuid IS NULL OR m.tenant_id = @tenant::uuid)
                LIMIT 1
                """;
            AddParam(cmd, "numero", numero);
            AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Null cubre a la vez «no existe» y «es de otro tenant»: el endpoint traduce ambos al
                // mismo 404 para que la respuesta no permita deducir que el trámite existe en otra
                // compañía.
                return null;
            }

            var estado = reader.GetString(9);
            var mensajeNovedad = await reader.IsDBNullAsync(15, ct) ? null : reader.GetString(15);

            var marcas = new MarcasRecorrido(
                Recibido: reader.GetDateTime(10),
                ValidacionNegocio: await reader.IsDBNullAsync(11, ct) ? null : reader.GetDateTime(11),
                ConsultaFuentes: await reader.IsDBNullAsync(12, ct) ? null : reader.GetDateTime(12),
                BorradorCreado: await reader.IsDBNullAsync(13, ct) ? null : reader.GetDateTime(13),
                Anulado: await reader.IsDBNullAsync(14, ct) ? null : reader.GetDateTime(14),
                Estado: estado,
                MensajeNovedad: mensajeNovedad,
                // La hora la pone el motor y no el proceso: así los deltas y las marcas se miden con el
                // mismo reloj aunque la aplicación y la base estén en máquinas distintas.
                Ahora: reader.GetDateTime(19));

            var (hitos, tiempos) = CalculadoraDeRecorrido.Construir(marcas);

            return new RecorridoTramite(
                Id: reader.GetGuid(0),
                Numero: reader.GetInt64(1),
                ReferenciaCliente: await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
                Placa: reader.GetString(3),
                Vin: await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4),
                TipoTramite: await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5),
                Operacion: await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                ClientTenantId: reader.GetGuid(7),
                Compania: await reader.IsDBNullAsync(8, ct) ? null : reader.GetString(8),
                Estado: estado,
                Hitos: hitos,
                Tiempos: tiempos,
                MensajeNovedad: mensajeNovedad,
                ProcedureInstanceId: await reader.IsDBNullAsync(16, ct) ? null : reader.GetGuid(16),
                CodigoOrganismoTransito: await reader.IsDBNullAsync(17, ct) ? null : reader.GetString(17),
                OrganismoTransito: await reader.IsDBNullAsync(18, ct) ? null : reader.GetString(18));
        }
        finally
        {
            await ResetTenantGucAsync(connection);
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task SetTenantGucAsync(DbConnection connection, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        AddParam(cmd, "tenant", tenantId?.ToString() ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ResetTenantGucAsync(DbConnection connection)
    {
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                return;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false)";
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // limpiar el GUC es best-effort; nunca debe romper la consulta
        catch (Exception)
        {
            // best-effort
        }
#pragma warning restore CA1031
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
