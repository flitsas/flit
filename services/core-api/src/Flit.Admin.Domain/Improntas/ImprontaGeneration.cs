namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Registro de auditoría de una generación de Certificado de Improntas Digitales vehicular
/// (Res. 17145/2023 Mintransporte) — <c>admin.impronta_generations</c> (HU #10466 / ADR-0022).
/// Log append-only: no lleva soft-delete ni <c>row_version</c> (mismo patrón que
/// <see cref="Flit.Admin.Domain.OtWebhooks.OtApiCallLog"/>). El PDF completo se conserva como
/// evidencia regulatoria exacta del certificado emitido por Kyverum RUNT.
/// </summary>
public sealed class ImprontaGeneration
{
    public Guid Id { get; init; }

    /// <summary>Tenant que generó la impronta. Trazabilidad — no es control de acceso (sin RLS, ADR-0022).</summary>
    public Guid TenantId { get; init; }

    /// <summary>Usuario FLIT (SuperAdmin) que generó la impronta.</summary>
    public Guid FlitUserId { get; init; }

    /// <summary>Radicado interno devuelto por Kyverum RUNT (formato <c>IMPR-XXXXXXXX</c>), único.</summary>
    public string Radicado { get; init; } = string.Empty;

    /// <summary>Hash SHA-256 impreso en el certificado, reproducible por un tercero a partir de los datos.</summary>
    public string HashSha256 { get; init; } = string.Empty;

    public DateTimeOffset FechaImpresa { get; init; }

    public string Placa { get; init; } = string.Empty;

    /// <summary>Al menos uno de NumMotor/NumChasis/NumSerie debe estar presente (regla de negocio).</summary>
    public string? NumMotor { get; init; }

    public string? NumChasis { get; init; }

    public string? NumSerie { get; init; }

    public string? Marca { get; init; }

    public string? Linea { get; init; }

    public string? Modelo { get; init; }

    public string OrgNombre { get; init; } = string.Empty;

    public string OrgNit { get; init; } = string.Empty;

    public string OrgCiudad { get; init; } = string.Empty;

    public string Operador { get; init; } = string.Empty;

    /// <summary>PDF completo devuelto por Kyverum, ya decodificado del Data URI base64 (Opción 1, ADR-0022).</summary>
    public byte[] PdfContent { get; init; } = [];

    public int PdfSizeBytes { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Valida el invariante de negocio "al menos un identificador de vehículo" (respaldado también por el
    /// CHECK <c>ck_impronta_generations_identificador_vehiculo</c> en BD como segunda línea de defensa).
    /// </summary>
    public bool TieneIdentificadorVehiculo() =>
        !string.IsNullOrWhiteSpace(NumMotor)
        || !string.IsNullOrWhiteSpace(NumChasis)
        || !string.IsNullOrWhiteSpace(NumSerie);
}
