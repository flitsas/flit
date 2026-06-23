namespace Flit.Admin.Application.OtClientProcedures.ListOtClientProcedures;

public sealed class ListOtClientProceduresQuery
{
    public Guid OtTenantId { get; init; }

    public string? Status { get; init; }

    public Guid? ProcedureTypeId { get; init; }

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
