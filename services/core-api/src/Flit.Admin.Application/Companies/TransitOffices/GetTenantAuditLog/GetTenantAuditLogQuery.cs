namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>Consulta paginada del historial de auditoría de gobernanza (AC4) con filtros opcionales (HU #14004).</summary>
public sealed class GetTenantAuditLogQuery
{
    public required Guid TenantId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    /// <summary>Día local (America/Bogota) inicial, inclusivo.</summary>
    public DateOnly? FechaDesde { get; init; }

    /// <summary>Día local (America/Bogota) final, inclusivo (incluye todo ese día).</summary>
    public DateOnly? FechaHasta { get; init; }

    /// <summary>Campo modificado (contiene, sin distinguir mayúsculas).</summary>
    public string? Campo { get; init; }

    /// <summary>Valor anterior (contiene, sin distinguir mayúsculas).</summary>
    public string? ValorAnterior { get; init; }

    /// <summary>Operador: id de usuario, nombre o correo (contiene, sin distinguir mayúsculas).</summary>
    public string? Operador { get; init; }
}
