namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Filtros de consulta de trámites de clientes OT (HU #10217 AC1/AC5).</summary>
public sealed class OtClientProcedureFilter
{
    public string? Status { get; init; }

    public Guid? ProcedureTypeId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
