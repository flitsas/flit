using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Revelado auditado de los datos personales de un trámite (HU #11820).
/// </summary>
/// <remarks>
/// <para>
/// El registro de auditoría se escribe ANTES de devolver los datos y en la misma transacción que
/// la lectura. Si se escribiera después, un fallo de red o un cierre del proceso entre ambos pasos
/// dejaría datos entregados sin rastro, que es exactamente el caso que este control existe para
/// impedir. Al ir dentro de la transacción, o hay constancia y hay datos, o no hay ninguna de las
/// dos cosas.
/// </para>
/// <para>
/// Es la única escritura de todo el Feature.
/// </para>
/// </remarks>
public sealed class DbRevelarDatosPersonalesRepository(IctDbContext db) : IRevelarDatosPersonalesQuery
{
    public async Task<DatosPersonalesRevelados?> RevelarAsync(
        long numero, Guid? tenantId, SolicitanteRevelado solicitante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitante);

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);
            await using var tx = await connection.BeginTransactionAsync(ct);

            Guid masterId;
            Guid tenantDelTramite;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT id, tenant_id FROM ict.external_integration_master
                    WHERE transaction_number = @numero
                      AND deleted_at IS NULL
                      AND (@tenant::uuid IS NULL OR tenant_id = @tenant::uuid)
                    LIMIT 1
                    """;
                AddParam(cmd, "numero", numero);
                AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    // Ni se revela ni se audita: no hay nada que revelar y registrar el intento sobre
                    // un trámite ajeno solo ensuciaría la auditoría con ruido.
                    await tx.RollbackAsync(ct);
                    return null;
                }

                masterId = reader.GetGuid(0);
                tenantDelTramite = reader.GetGuid(1);
            }

            await RegistrarAccesoAsync(connection, tx, tenantDelTramite, masterId, numero, solicitante, ct);
            var secciones = await LeerActoresEnClaroAsync(connection, tx, masterId, ct);

            await tx.CommitAsync(ct);
            return new DatosPersonalesRevelados(numero, secciones, Auditado: true);
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

    private static async Task RegistrarAccesoAsync(
        DbConnection connection, DbTransaction tx, Guid tenantId, Guid masterId, long numero,
        SolicitanteRevelado solicitante, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        // No se guarda el valor revelado a propósito: registrar el dato en claro para auditar quién
        // lo vio en claro multiplicaría el problema en vez de controlarlo.
        cmd.CommandText = """
            INSERT INTO ict.pii_reveal_audit
                (tenant_id, master_id, transaction_number, requested_by, requested_role, scope)
            VALUES (@tenant, @master, @numero, @sujeto, @rol, 'actores')
            """;
        AddParam(cmd, "tenant", tenantId);
        AddParam(cmd, "master", masterId);
        AddParam(cmd, "numero", numero);
        AddParam(cmd, "sujeto", Recortar(solicitante.Sujeto, 120));
        AddParam(cmd, "rol", Recortar(solicitante.Rol, 60));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<SeccionDatos>> LeerActoresEnClaroAsync(
        DbConnection connection, DbTransaction tx, Guid masterId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT actor_type, document_type, document_number, name, first_last_name, second_last_name,
                   phone, email, city, state, address,
                   legal_representative_name, legal_representative_document_type,
                   legal_representative_document_number, legal_representative_email
            FROM ict.external_integration_actors
            WHERE master_id = @master AND deleted_at IS NULL
            ORDER BY actor_type
            """;
        AddParam(cmd, "master", masterId);

        var secciones = new List<SeccionDatos>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var nombre = string.Join(" ", new[] { Texto(reader, 3), Texto(reader, 4), Texto(reader, 5) }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

            var datos = new List<DatoTramite>
            {
                new("Nombre", nombre.Length == 0 ? null : nombre, EsSensible: true),
                new("Documento", Documento(Texto(reader, 1), Texto(reader, 2)), EsSensible: true),
                new("Teléfono", Texto(reader, 6), EsSensible: true),
                new("Correo", Texto(reader, 7), EsSensible: true),
                new("Ciudad", Texto(reader, 8)),
                new("Departamento", Texto(reader, 9)),
                new("Dirección", Texto(reader, 10), EsSensible: true),
            };

            var repLegal = Texto(reader, 11);
            if (!string.IsNullOrWhiteSpace(repLegal))
            {
                datos.Add(new DatoTramite("Representante legal", repLegal, EsSensible: true));
                datos.Add(new DatoTramite("Documento del representante",
                    Documento(Texto(reader, 12), Texto(reader, 13)), EsSensible: true));
                datos.Add(new DatoTramite("Correo del representante", Texto(reader, 14), EsSensible: true));
            }

            secciones.Add(new SeccionDatos(EtiquetasDetalle.Actor(Texto(reader, 0)), datos));
        }

        return secciones;
    }

    private static string? Texto(DbDataReader reader, int i) =>
        reader.IsDBNull(i) ? null : reader.GetString(i) is { Length: > 0 } s ? s : null;

    private static string? Documento(string? tipo, string? numero) =>
        string.IsNullOrWhiteSpace(numero) ? null
        : string.IsNullOrWhiteSpace(tipo) ? numero
        : $"{tipo} {numero}";

    private static string Recortar(string? valor, int maximo)
    {
        var limpio = (valor ?? string.Empty).Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo];
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
