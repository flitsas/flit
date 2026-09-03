namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Filtros y ordenamiento de trámites de clientes OT (HU #10217 AC1/AC5).</summary>
public sealed class OtClientProcedureFilter
{
    public string? Status { get; init; }

    /// <summary>
    /// Sub-estado de la ruta de placa. Acepta varios separados por coma
    /// (<c>asignado,terminado</c>) y el valor especial <c>sin_ruta</c> para los trámites que NO
    /// están en ruta de placa (columna nula).
    ///
    /// <para>
    /// Existe porque las tarjetas de la cabecera se pulsan para filtrar, y tres de ellas —"Sin
    /// asignar placa", "Con placa asignada" y "Sin gestión"— no son estados del ciclo de vida sino
    /// del sub-flujo de placa. Sin este filtro, pulsarlas habría llevado a una lista que no era la
    /// que la tarjeta acababa de contar.
    /// </para>
    /// </summary>
    public string? PlateFlowStatus { get; init; }

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
