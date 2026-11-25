using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Platform.Application.TenantProducts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1, DDL 205) — <c>platform.tenant_product_provisioning</c>: el último estado que reporta
/// el servicio del producto. El guardado es un solo
/// <c>INSERT … ON CONFLICT … DO UPDATE … WHERE reported_version &lt; EXCLUDED.reported_version</c>, así dos reportes concurrentes
/// no se pisan y una versión igual o menor no cambia nada. Filtro explícito por <c>tenant_id</c> (sin <c>HasQueryFilter</c>,
/// convención del repo). Columnas en inglés; el registro de dominio conserva los nombres del contrato gRPC. Una FK rota
/// (<c>23503</c>) sale como error de dominio: producto fuera del catálogo ⇒ <see cref="TenantProductException.ProductNotFound"/>;
/// empresa borrada entre la comprobación y el guardado ⇒ <c>EmpresaExiste = false</c>.
/// </summary>
internal sealed class TenantProductProvisioningRepository(IIdentityDb context) : ITenantProductProvisioningStore
{
    private readonly IIdentityDb _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporte);

        if (!await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == reporte.TenantId, cancellationToken).ConfigureAwait(false))
            return new ProvisioningGuardado(EmpresaExiste: false, Aplicado: false, VersionGuardada: 0);

        List<long> aplicadas;
        try
        {
            // Parametrizado (FormattableString): EF crea un parámetro por cada valor interpolado.
            aplicadas = await _context.Database.SqlQuery<long>($"""
                INSERT INTO platform.tenant_product_provisioning
                       (tenant_id, product_code, status, reason_code, message, action, signature_confirmed, reported_version, reported_at)
                VALUES ({reporte.TenantId}, {reporte.ProductCode}, {reporte.Estado}, {reporte.MotivoCodigo}, {reporte.Mensaje},
                        {reporte.Accion}, {reporte.FirmaConfirmada}, {reporte.Version}, {reporte.ActualizadoEn})
                ON CONFLICT (tenant_id, product_code) DO UPDATE
                   SET status = EXCLUDED.status,
                       reason_code = EXCLUDED.reason_code,
                       message = EXCLUDED.message,
                       action = EXCLUDED.action,
                       signature_confirmed = EXCLUDED.signature_confirmed,
                       reported_version = EXCLUDED.reported_version,
                       reported_at = EXCLUDED.reported_at
                 WHERE platform.tenant_product_provisioning.reported_version < EXCLUDED.reported_version
                RETURNING reported_version AS "Value"
                """).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (FkRota(ex) is { } fk)
        {
            // La empresa se borró entre la comprobación de arriba y el guardado: igual que si no existiera.
            if (string.Equals(fk.ConstraintName, FkEmpresa, StringComparison.Ordinal))
                return new ProvisioningGuardado(EmpresaExiste: false, Aplicado: false, VersionGuardada: 0);
            throw new TenantProductException(
                TenantProductException.ProductNotFound, $"El producto '{reporte.ProductCode}' no existe.");
        }

        if (aplicadas.Count == 1)
            return new ProvisioningGuardado(EmpresaExiste: true, Aplicado: true, VersionGuardada: aplicadas[0]);

        var vigente = await _context.Set<TenantProductProvisioningEntity>().AsNoTracking()
            .Where(p => p.TenantId == reporte.TenantId && p.ProductCode == reporte.ProductCode)
            .Select(p => p.ReportedVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ProvisioningGuardado(EmpresaExiste: true, Aplicado: false, VersionGuardada: vigente);
    }

    public async Task<IReadOnlyList<TenantProductProvisioning>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<TenantProductProvisioningEntity>().AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.ProductCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(p => new TenantProductProvisioning(
                p.TenantId, p.ProductCode, p.Status, p.ReasonCode, p.Message, p.Action, p.SignatureConfirmed, p.ReportedVersion, p.ReportedAt))
            .ToList();
    }

    /// <summary>FK de la empresa (DDL 205); la otra FK de la tabla es la del catálogo de productos.</summary>
    private const string FkEmpresa = "fk_tenant_product_provisioning_tenants";

    /// <summary>La violación de FK (<c>23503</c>) de la consulta, venga sola o envuelta por EF.</summary>
    private static PostgresException? FkRota(Exception ex) =>
        (ex as PostgresException ?? ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.ForeignKeyViolation } pg ? pg : null;
}
