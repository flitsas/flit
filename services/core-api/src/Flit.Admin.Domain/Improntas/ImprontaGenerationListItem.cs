namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Proyección de metadata de una generación de impronta para el listado paginado
/// (HU #10468 / ADR-0022). Deliberadamente NO incluye <see cref="ImprontaGeneration.PdfContent"/>:
/// el listado nunca debe arrastrar el binario del certificado (ver ADR-0022, "Lo que se pierde" —
/// disciplina de proyección explícita).
/// </summary>
public sealed class ImprontaGenerationListItem
{
    public Guid Id { get; init; }

    /// <summary>Tenant que generó la impronta. Trazabilidad — no es control de acceso (sin RLS, ADR-0022).</summary>
    public Guid TenantId { get; init; }

    /// <summary>Usuario FLIT (SuperAdmin) que generó la impronta.</summary>
    public Guid FlitUserId { get; init; }

    public string Radicado { get; init; } = string.Empty;

    public string HashSha256 { get; init; } = string.Empty;

    public DateTimeOffset FechaImpresa { get; init; }

    public string Placa { get; init; } = string.Empty;

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

    /// <summary>Tamaño en bytes del PDF persistido — sustituto liviano del binario para la UI.</summary>
    public int PdfSizeBytes { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
