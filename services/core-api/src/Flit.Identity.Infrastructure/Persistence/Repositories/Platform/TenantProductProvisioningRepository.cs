using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1, DDL 205) — <c>platform.tenant_product_provisioning</c>: el último estado que reporta
/// el servicio del producto. El guardado es un solo <c>INSERT … ON CONFLICT … DO UPDATE … WHERE version &lt; EXCLUDED.version</c>,
/// así dos reportes concurrentes no se pisan y una versión igual o menor no cambia nada. Filtro explícito por
/// <c>tenant_id</c> (sin <c>HasQueryFilter</c>, convención del repo).
/// </summary>
internal sealed class TenantProductProvisioningRepository(IIdentityDb context) : ITenantProductProvisioningStore
{
    private readonly IIdentityDb _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ProvisioningGuardado> GuardarSiEsMasNuevoAsync(TenantProductProvisioning reporte, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reporte);

        if (!await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == reporte.TenantId, cancellationToken).ConfigureAwait(false))
            return new ProvisioningGuardado(EmpresaExiste: false, Aplicado: false, VersionGuardada: 0);

        // Parametrizado (FormattableString): EF crea un parámetro por cada valor interpolado.
        var aplicadas = await _context.Database.SqlQuery<long>($"""
            INSERT INTO platform.tenant_product_provisioning
                   (tenant_id, product_code, estado, motivo_codigo, mensaje, accion, firma_confirmada, version, actualizado_en)
            VALUES ({reporte.TenantId}, {reporte.ProductCode}, {reporte.Estado}, {reporte.MotivoCodigo}, {reporte.Mensaje},
                    {reporte.Accion}, {reporte.FirmaConfirmada}, {reporte.Version}, {reporte.ActualizadoEn})
            ON CONFLICT (tenant_id, product_code) DO UPDATE
               SET estado = EXCLUDED.estado,
                   motivo_codigo = EXCLUDED.motivo_codigo,
                   mensaje = EXCLUDED.mensaje,
                   accion = EXCLUDED.accion,
                   firma_confirmada = EXCLUDED.firma_confirmada,
                   version = EXCLUDED.version,
                   actualizado_en = EXCLUDED.actualizado_en
             WHERE platform.tenant_product_provisioning.version < EXCLUDED.version
            RETURNING version AS "Value"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        if (aplicadas.Count == 1)
            return new ProvisioningGuardado(EmpresaExiste: true, Aplicado: true, VersionGuardada: aplicadas[0]);

        var vigente = await _context.Set<TenantProductProvisioningEntity>().AsNoTracking()
            .Where(p => p.TenantId == reporte.TenantId && p.ProductCode == reporte.ProductCode)
            .Select(p => p.Version)
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
                p.TenantId, p.ProductCode, p.Estado, p.MotivoCodigo, p.Mensaje, p.Accion, p.FirmaConfirmada, p.Version, p.ActualizadoEn))
            .ToList();
    }
}
