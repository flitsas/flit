using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Npgsql;

namespace Flit.DataMigration.V1.Loading;

/// <summary>
/// Crea en V2 el tenant correspondiente a una compañía de V1.
/// <para>
/// SOLO para el laboratorio local. En producción esto debe estar DESACTIVADO
/// (<c>CreateTenantIfMissing: false</c>): allí un NIT sin tenant es una señal de que falta
/// dar de alta al cliente, y crear uno automáticamente escondería el problema y podría
/// duplicar empresas que ya existen con otro formato de NIT.
/// </para>
/// </summary>
public sealed class TenantProvisioner(FlitDbContext db, string v1ConnectionString)
{
    /// <summary>
    /// Devuelve el tenant creado —no solo su id— porque quien llama debe re-indexarlo en el
    /// <see cref="TenantResolver"/>: si no, el siguiente trámite de la misma compañía intentaría
    /// crearlo otra vez y chocaría contra <c>uq_tenants_code</c>.
    /// </summary>
    public async Task<Tenant> CreateFromV1Async(string nit, CancellationToken cancellationToken)
    {
        var legalName = await LookupCompanyNameAsync(nit, cancellationToken) ?? $"Compañía V1 {nit}";

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Code = $"V1-{nit}",
            LegalName = legalName,
            TaxId = nit,
            TenantType = "RENTING",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return tenant;
    }

    /// <summary>Razón social de la compañía según <c>company_parameters</c> de V1.</summary>
    private async Task<string?> LookupCompanyNameAsync(string nit, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(v1ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT company_name FROM company_parameters WHERE company_nit = @nit LIMIT 1", connection);
        command.Parameters.AddWithValue("nit", nit);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
