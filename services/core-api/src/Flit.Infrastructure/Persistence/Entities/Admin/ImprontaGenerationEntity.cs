namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Historial de generación de improntas — <c>admin.impronta_generations</c> (HU #10466 / ADR-0022).
/// Tabla append-only (sin <c>updated_at/by</c>, <c>deleted_at/by</c> ni <c>row_version</c>, mismo patrón
/// que <see cref="OtApiCallLogEntity"/>) y SIN RLS (dispensa documentada de A10: vista global exclusiva
/// de SuperAdmin, análoga a <c>AnalyticsReadRepository</c> — <c>TenantId</c> es trazabilidad, no control
/// de acceso).
/// </summary>
public sealed class ImprontaGenerationEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid FlitUserId { get; set; }

    public string Radicado { get; set; } = string.Empty;

    public string HashSha256 { get; set; } = string.Empty;

    public DateTimeOffset FechaImpresa { get; set; }

    public string Placa { get; set; } = string.Empty;

    public string? NumMotor { get; set; }

    public string? NumChasis { get; set; }

    public string? NumSerie { get; set; }

    public string? Marca { get; set; }

    public string? Linea { get; set; }

    public string? Modelo { get; set; }

    public string OrgNombre { get; set; } = string.Empty;

    public string OrgNit { get; set; } = string.Empty;

    public string OrgCiudad { get; set; } = string.Empty;

    public string Operador { get; set; } = string.Empty;

    public byte[] PdfContent { get; set; } = [];

    public int PdfSizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
