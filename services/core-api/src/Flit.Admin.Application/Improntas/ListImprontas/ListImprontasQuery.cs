namespace Flit.Admin.Application.Improntas.ListImprontas;

/// <summary>
/// Petición del listado paginado del historial de improntas (HU #10468). Refleja los
/// parámetros de query string de <c>GET /api/v1/admin/improntas</c>. Todos los filtros
/// son opcionales.
/// </summary>
public sealed class ListImprontasQuery
{
    public string? Placa { get; init; }

    public string? Radicado { get; init; }

    /// <summary>Límite inferior (inclusive) del rango de fecha de creación. Opcional.</summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>Límite superior (inclusive) del rango de fecha de creación. Opcional.</summary>
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>Página solicitada. Si es nula o &lt; 1 se normaliza a 1.</summary>
    public int? Page { get; init; }

    /// <summary>Tamaño de página. Si es nulo o excede el máximo se normaliza.</summary>
    public int? PageSize { get; init; }
}
