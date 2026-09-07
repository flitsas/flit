namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Campos por los que se puede ordenar el listado de trámites EN SQL. El parámetro público
/// <c>sortBy</c> (string, de la query del endpoint) se valida contra una lista blanca ANTES de llegar
/// aquí (ver <c>Application.UseCases.ProcedureInstances.ProcedureInstanceSortFields.Resolve</c>): un
/// valor no reconocido cae a <see cref="Default"/>, nunca se concatena crudo en la consulta.
/// </summary>
public enum ProcedureInstanceSortBy
{
    /// <summary>Orden por defecto histórico: prioritario primero, luego creación descendente.</summary>
    Default = 0,
    Comprador,
    CreatedAt,
    UpdatedAt,
    Gestor,
    Placa,
    Vin,
    // HU #12108 — subcampos de las celdas compuestas del listado: la cabecera de "Radicado",
    // "Vehículo" y "Trámite" deja elegir por cuál de sus datos se ordena, y sin estas claves la
    // mitad de las opciones del desplegable no ordenarían nada.
    Radicado,
    Estado,
    TipoTramite,
    Fuente,
}

/// <summary>Dirección de ordenamiento pedida por el caller.</summary>
public enum SortDirection
{
    Ascending = 0,
    Descending = 1,
}
