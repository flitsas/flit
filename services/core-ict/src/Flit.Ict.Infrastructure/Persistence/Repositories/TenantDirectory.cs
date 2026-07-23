using System.Data;
using Flit.Ict.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura de <c>identity.tenants</c> (schema propiedad de core-api) para resolver la compañía en el
/// login ICT. Consulta parametrizada de solo lectura a través de la conexión del DbContext.
/// </summary>
public sealed class TenantDirectory(IctDbContext db) : ITenantDirectory
{
    public async Task<TenantInfo?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id, code, legal_name, tax_id, is_active FROM identity.tenants WHERE id = @id AND deleted_at IS NULL";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = tenantId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            return new TenantInfo(
                Id: reader.GetGuid(0),
                Code: reader.GetString(1),
                LegalName: reader.GetString(2),
                TaxId: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                IsActive: reader.GetBoolean(4));
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
