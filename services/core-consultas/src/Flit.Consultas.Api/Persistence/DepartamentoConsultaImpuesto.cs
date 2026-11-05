namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Departamento cuya consulta de impuesto está encendida o apagada (<c>consultas.vehicle_tax_departments</c>, ADR-0080).
/// Catálogo global, sin empresa. La lista efectiva es <see cref="IsActive"/> con <see cref="DeletedAt"/> nulo.
/// </summary>
public sealed class DepartamentoConsultaImpuesto
{
    public Guid Id { get; set; }

    /// <summary>Código DIVIPOLA de dos dígitos.</summary>
    public string DepartmentCode { get; set; } = string.Empty;

    /// <summary>Nombre del departamento, como <c>traffic_secretary_department</c> de la tabla legada.</summary>
    public string DepartmentName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Apagado. La fila se conserva para volver a encenderla.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public string? ExternalRefs { get; set; }
}
