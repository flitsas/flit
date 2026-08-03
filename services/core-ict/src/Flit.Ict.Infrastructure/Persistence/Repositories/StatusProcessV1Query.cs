using System.Data;
using System.Data.Common;
using System.Globalization;
using Flit.Ict.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consulta del estado en la forma v1 (compatibilidad de clientes). Calca el repositorio de v1:
/// arma la respuesta desde el histórico <c>ict.external_integration_process_status</c>. El lookup es
/// por manager_id_transaction (el TransactionFlit que devuelve el registro).
/// </summary>
public sealed class StatusProcessV1Query(IctDbContext db) : IStatusProcessV1Query
{
    public async Task<StatusProcessV1Response?> GetByManagerIdTransactionAsync(
        string reference,
        Guid tenantId,
        CancellationToken ct = default)
    {
        // La referencia puede ser el número secuencial (transaction_number, paridad v1) o el
        // manager_id_transaction propio del gestor; se prioriza el número cuando es numérica.
        var number = long.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n
            : (long?)null;
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);

            var (masterId, companyDocument, transactionNumber) =
                await FindMasterAsync(connection, reference, number, tenantId, ct);
            if (masterId is null)
            {
                return null;
            }

            var history = await ReadHistoryAsync(connection, masterId.Value, ct);
            if (history.Count == 0)
            {
                return null;
            }

            var latest = history[0];
            var items = history
                .Select(h => new StatusProcessV1Item(
                    Status: h.Code,
                    StatusDescription: Describe(h.Code),
                    Observation: h.Observation,
                    UserMailRegistered: h.Mail,
                    UserRoleRegistered: h.Role,
                    UserNameRegistered: h.UserName,
                    DateRegistered: h.Date,
                    CompanyRegistered: string.IsNullOrEmpty(h.Company) ? companyDocument : h.Company))
                .ToList();

            return new StatusProcessV1Response(
                TransactionFlit: transactionNumber.ToString(CultureInfo.InvariantCulture),
                StatusValidation: latest.Code,
                StatusDescription: Describe(latest.Code),
                MessageValidation: latest.Message,
                StatusObservation: latest.Observation,
                Status: latest.Code,
                StatusMessage: latest.Message,
                StatusProcess: items);
        }
        finally
        {
            // Defensa en profundidad: limpiar el GUC de tenant para no dejarlo en una conexión que vuelve
            // al pool (Npgsql además resetea al cerrar; esto cubre el caso wasClosed=false).
            await ResetTenantGucAsync(connection);
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed record HistoryRow(int Code, string Message, string Observation, string Mail, string Role, string UserName, string Date, string Company);

    private static async Task<(Guid? MasterId, string CompanyDocument, long TransactionNumber)> FindMasterAsync(
        DbConnection connection, string reference, long? number, Guid tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_manager_document, transaction_number FROM ict.external_integration_master " +
            "WHERE ((@num IS NOT NULL AND transaction_number = @num) OR manager_id_transaction = @flit) " +
            "AND tenant_id = @tenant AND deleted_at IS NULL " +
            "ORDER BY (transaction_number = @num) DESC NULLS LAST LIMIT 1";
        AddParam(cmd, "flit", reference);
        AddParam(cmd, "num", (object?)number ?? DBNull.Value);
        AddParam(cmd, "tenant", tenantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return (null, string.Empty, 0);
        }

        return (reader.GetGuid(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), reader.GetInt64(2));
    }

    private static async Task<List<HistoryRow>> ReadHistoryAsync(DbConnection connection, Guid masterId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id_parprosta, s.message_validation, s.status_process_observation,
                   s.status_process_mail_userregistered, s.status_process_role_userregistered,
                   s.status_process_registrationdate, s.status_process_company_registered,
                   s.status_process_userregistered
            FROM ict.external_integration_process_status s
            WHERE s.id_eimas = @masterId
            ORDER BY s.status_process_registrationdate DESC, s.created_at DESC
            """;
        AddParam(cmd, "masterId", masterId);
        var list = new List<HistoryRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new HistoryRow(
                Code: reader.GetInt16(0),
                Message: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Observation: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Mail: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Role: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Date: reader.IsDBNull(5) ? string.Empty : reader.GetDateTime(5).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Company: reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                UserName: reader.IsDBNull(7) ? string.Empty : reader.GetString(7)));
        }

        return list;
    }

    // El GUC se fija con scope de SESIÓN (is_local=false) porque la consulta corre en varios statements
    // sobre la MISMA conexión sin transacción explícita; con is_local=true no persistiría entre statements
    // y RLS no vería el tenant. Se re-fija en cada llamada antes de leer y se limpia al terminar (finally).
    private static async Task SetTenantGucAsync(DbConnection connection, Guid tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        AddParam(cmd, "tenant", tenantId.ToString());
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

    /// <summary>Descripción v1 exacta por código de estado (para no depender de la fila del catálogo).</summary>
    private static string Describe(int code) => code switch
    {
        1 => "Registrado",
        2 => "En Validacion",
        3 => "Procesado",
        4 => "Con Novedades",
        5 => "Borrador",
        6 => "Anulado",
        _ => string.Empty,
    };

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
