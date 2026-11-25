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

    /// <summary>Estado que define el producto (en impuestos, los del plan §3.1). Columna <c>status</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Código estable del motivo. Columna <c>reason_code</c>.</summary>
    public string? ReasonCode { get; set; }

    /// <summary>Motivo legible. Columna <c>message</c>.</summary>
    public string? Message { get; set; }

    /// <summary>Acción del SuperAdmin. Columna <c>action</c>.</summary>
    public string? Action { get; set; }

    /// <summary>Solo en LISTO: false = «Firma sin confirmar». Null en los demás estados. Columna <c>signature_confirmed</c>.</summary>
    public bool? SignatureConfirmed { get; set; }

    /// <summary>Versión monotónica del estado en el servicio del producto (no es <see cref="RowVersion"/>). Columna <c>reported_version</c>.</summary>
    public long ReportedVersion { get; set; }

    /// <summary>Momento del estado. Columna <c>reported_at</c>.</summary>
    public DateTimeOffset ReportedAt { get; set; }

    /// <summary>HU #14711 (DDL 206) — intentos automáticos fallidos del alta que escribe la plataforma. Columna <c>attempts</c>.</summary>
    public int Attempts { get; set; }

    /// <summary>HU #14711 (DDL 206) — próximo intento (o fin de la reserva) del alta automática. Columna <c>next_attempt_at</c>.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long RowVersion { get; set; }
}
