using Flit.Queries.Domain;

namespace Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;

public sealed class ListOtClientProceduresQuery
{
    public Guid OtTenantId { get; init; }

    public Guid? TransitOfficeId { get; init; }

    /// <summary>Estado del ciclo de vida; varios separados por coma (ADR-0059).</summary>
    public string? Status { get; init; }

    /// <summary>
    /// Pedido del usuario (2026-09-16) — filtro de la tarjeta "Solicitudes de revocatoria": trámites
    /// con una solicitud de revocatoria ACTIVA (<c>solicitada</c>/<c>en_revision</c>).
    /// </summary>
    public bool? HasActiveRevocationRequest { get; init; }
    public Guid? ProcedureTypeId { get; init; }

    /// <summary>Epic #12686 — pestaña de familia (<c>MATRICULAS</c>/<c>TRASPASO</c>/<c>OTROS</c>).</summary>
    public string? Familia { get; init; }

    public string? Vin { get; init; }

    public string? Placa { get; init; }

    public string? Vendedor { get; init; }

    public string? Comprador { get; init; }

    public string? Gestor { get; init; }

    /// <summary>Texto libre transversal de la barra de búsqueda (HU #12218).</summary>
    public string? Busqueda { get; init; }

    /// <summary>
    /// Condiciones de la gramática de Consultas (HU #12217). Llegan solo por el endpoint POST: placa,
    /// VIN y radicado admiten pegar una lista completa desde Excel, y unos cientos de valores no
    /// caben en una query string.
    /// </summary>
    public IReadOnlyList<QueryCondition>? Condiciones { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    public DateTimeOffset? UpdatedFrom { get; init; }

    public DateTimeOffset? UpdatedTo { get; init; }

    public string? SortBy { get; init; }

    public string? SortDir { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }
}

public sealed class ListOtClientProceduresResult
{
    public IReadOnlyList<OtClientProcedureResponse> Data { get; init; } = [];

    public long TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}
