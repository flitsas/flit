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
