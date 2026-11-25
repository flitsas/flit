namespace Flit.Infrastructure.Persistence.Entities.Platform;

/// <summary>
/// Último estado de aprovisionamiento de un producto por empresa — <c>platform.tenant_product_provisioning</c> (HU #14694,
/// ADR-0093, DDL 205). Lo escribe core-identity con lo que reporta el servicio del producto (solo versiones mayores) y lo
/// lee el listado de productos del admin de compañías. Sin datos personales ni secretos.
/// </summary>
public sealed class TenantProductProvisioningEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Estado que define el producto (en impuestos, los del plan §3.1).</summary>
    public string Estado { get; set; } = string.Empty;

    public string? MotivoCodigo { get; set; }

    public string? Mensaje { get; set; }

    public string? Accion { get; set; }

    /// <summary>Solo en LISTO: false = «Firma sin confirmar». Null en los demás estados.</summary>
    public bool? FirmaConfirmada { get; set; }

    /// <summary>Versión monotónica del estado en el servicio del producto (no es <see cref="RowVersion"/>).</summary>
    public long Version { get; set; }

    public DateTimeOffset ActualizadoEn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long RowVersion { get; set; }
}
