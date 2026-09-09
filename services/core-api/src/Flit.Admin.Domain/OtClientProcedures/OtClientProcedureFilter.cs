using Flit.Queries.Domain;

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
    /// Condiciones armadas con la gramática de Consultas (HU #12217): campo del catálogo, operador
    /// y valores. Se resuelven en <c>WHERE</c> sobre el universo de trámites con grant vigente, no
    /// sobre la página ya cargada.
    ///
    /// <para>Conviven con los filtros sueltos de arriba en vez de sustituirlos: <c>Status</c> y
    /// <c>PlateFlowStatus</c> los sigue mandando la tira de tarjetas de la cabecera, que no es un
    /// filtro que el usuario escriba sino un atajo a un recuento ya hecho.</para>
    ///
    /// <para>El endpoint rechaza con 400 cualquier campo que no esté en
    /// <c>OtBandejaQueryFieldCatalog</c>; el repositorio, por su parte, ignora el desconocido en vez
    /// de lanzar, porque llegar aquí con uno significaría que catálogo y traductor se
    /// desincronizaron y tumbar la bandeja sería peor que filtrar de menos.</para>
    /// </summary>
    public IReadOnlyList<QueryCondition>? Condiciones { get; init; }

    /// <summary>Rango sobre la fecha de radicación (cuándo entró el trámite al organismo).</summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>Rango sobre la fecha del último movimiento.</summary>
    public DateTimeOffset? UpdatedFrom { get; init; }

    public DateTimeOffset? UpdatedTo { get; init; }

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
