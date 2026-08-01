namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Filtros y ordenamiento de trámites de clientes OT (HU #10217 AC1/AC5).</summary>
public sealed class OtClientProcedureFilter
{
    public string? Status { get; init; }

    public Guid? ProcedureTypeId { get; init; }

    /// <summary>Filtro parcial por VIN (case-insensitive).</summary>
    public string? Vin { get; init; }

    /// <summary>Filtro parcial por placa (case-insensitive).</summary>
    public string? Placa { get; init; }

    /// <summary>Filtro parcial por propietario/vendedor.</summary>
    public string? Vendedor { get; init; }

    /// <summary>Filtro parcial por comprador.</summary>
    public string? Comprador { get; init; }

    /// <summary>Filtro parcial por nombre del gestor.</summary>
    public string? Gestor { get; init; }

    /// <summary>
    /// Columna de ordenamiento secundario (tras prioritario). Valores:
    /// <c>vin</c>, <c>placa</c>, <c>vendedor</c>, <c>comprador</c>, <c>gestor</c>,
    /// <c>createdAt</c> (default), <c>referenceNumber</c>, <c>status</c>, <c>procedureType</c>,
    /// <c>clientTenant</c>.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary><c>asc</c> o <c>desc</c> (default <c>desc</c> para fecha).</summary>
    public string? SortDir { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
