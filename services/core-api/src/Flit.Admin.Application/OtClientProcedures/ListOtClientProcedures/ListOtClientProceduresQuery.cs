using Flit.Queries.Domain;

namespace Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;

public sealed class ListOtClientProceduresQuery
{
    public Guid OtTenantId { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public string? Status { get; init; }

    /// <summary>Sub-estado de placa; varios por coma y `sin_ruta` para los que no la tienen.</summary>
    public string? PlateFlowStatus { get; init; }

    public Guid? ProcedureTypeId { get; init; }

    public string? Vin { get; init; }

    public string? Placa { get; init; }

    public string? Vendedor { get; init; }

    public string? Comprador { get; init; }

    public string? Gestor { get; init; }

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
