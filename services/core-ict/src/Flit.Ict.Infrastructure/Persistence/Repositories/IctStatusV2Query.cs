using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Estado v2-native: proyecta el <c>process_status_id</c> + flags que escriben los SP al vocabulario v2
/// (<see cref="IctEstado.Map"/>, Plano B) y, si el pre-trámite ya se materializó, adjunta el estado v2 del
/// trámite leyendo <c>tramites.procedure_instances.status</c> (Plano C). El lookup es por
/// manager_id_transaction, con el GUC de RLS fijado al tenant.
/// </summary>
public sealed class IctStatusV2Query(IctDbContext db) : IIctStatusV2Query
{
    public async Task<IctStatusV2Response?> GetByManagerIdTransactionAsync(
        string managerIdTransaction,
        Guid tenantId,
        CancellationToken ct = default)
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

            short processStatusId;
            short businessValidation;
            short externalValidation;
            Guid? procedureInstanceId;
            string comments;
            bool closedDocument;
            bool processWithoutAttachedDocuments;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT process_status_id, business_validation, external_validation,
                           procedure_instance_id, business_comments_validation,
                           closed_document, process_without_attached_documents
                    FROM ict.external_integration_master
                    WHERE manager_id_transaction = @flit AND tenant_id = @tenant AND deleted_at IS NULL
                    LIMIT 1
                    """;
                AddParam(cmd, "flit", managerIdTransaction);
                AddParam(cmd, "tenant", tenantId);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    return null;
                }

                processStatusId = reader.GetInt16(0);
                businessValidation = reader.GetInt16(1);
                externalValidation = reader.GetInt16(2);
                procedureInstanceId = await reader.IsDBNullAsync(3, ct) ? null : reader.GetGuid(3);
                comments = await reader.IsDBNullAsync(4, ct) ? string.Empty : reader.GetString(4);
                closedDocument = reader.GetBoolean(5);
                processWithoutAttachedDocuments = reader.GetBoolean(6);
            }

            var ictEstado = IctEstado.Map(
                processStatusId,
                hasProcedureInstance: procedureInstanceId is not null,
                businessValidated: businessValidation == 2,
                externalStarted: externalValidation >= 1);

            // Espera de adjuntos (paridad v1): mientras el documento no esté cerrado y no se haya declarado
            // el waiver, el pre-trámite NO materializa (lo retiene el gate de SendToCoreApiJob). Se lo
            // señalamos explícitamente al cliente para que sepa que debe cerrar el documento.
            if (procedureInstanceId is null && !closedDocument && !processWithoutAttachedDocuments
                && processStatusId is 1 or 2 && string.IsNullOrWhiteSpace(comments))
            {
                comments = "Pendiente de cierre de documentos: suba los adjuntos y envíe closed=true "
                    + "(POST /api/v1/transact-attachments/close/{transactionFlit}) para continuar a borrador.";
            }

            var tramiteStatus = procedureInstanceId is null
                ? null
                : await TryReadTramiteStatusAsync(connection, procedureInstanceId.Value, ct);

            return new IctStatusV2Response(managerIdTransaction, ictEstado, procedureInstanceId, tramiteStatus, comments);
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Lee el estado v2 del trámite materializado (Plano C). Best-effort: si el rol de core-ict no tiene
    /// SELECT sobre <c>tramites.procedure_instances</c> (grant cross-schema), degrada a null en vez de fallar.
    /// TODO(ICT-STATUS-GRANT): asegurar el grant de lectura en prod para que tramiteStatus no quede null.
    /// </summary>
    private static async Task<string?> TryReadTramiteStatusAsync(DbConnection connection, Guid procedureInstanceId, CancellationToken ct)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT status FROM tramites.procedure_instances WHERE id = @id LIMIT 1";
            AddParam(cmd, "id", procedureInstanceId);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result as string;
        }
        catch (DbException)
        {
            return null;
        }
    }

    private static async Task SetTenantGucAsync(DbConnection connection, Guid tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        AddParam(cmd, "tenant", tenantId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
