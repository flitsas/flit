namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Filtros server-side del listado de trámites, resueltos en SQL sobre <c>procedure_instances</c>
/// (columnas propias o denormalizadas — migración TramitesCamposBusqueda). Todos son opcionales;
/// <c>null</c>/vacío = sin filtrar por ese campo.
/// <para>
/// VIN y placa comparan por IGUALDAD case-insensitive (el usuario busca UN vehículo puntual);
/// vendedor/comprador/gestor son búsqueda por SUBCADENA (el usuario suele recordar solo una parte del
/// nombre).
/// </para>
/// </summary>
public sealed record ProcedureInstanceListFilter
{
    public string? Vin { get; init; }
    public string? Placa { get; init; }
    public string? Vendedor { get; init; }
    public string? Comprador { get; init; }
    public string? Gestor { get; init; }

    /// <summary>
    /// <c>true</c> = solo trámites con la firma ELECTRÓNICA de la compraventa COMPLETA (comprador y,
    /// si aplica —traspaso—, vendedor, ambos con <c>Estado = firmada</c>); <c>false</c> = solo con esa
    /// firma pendiente; <c>null</c> = sin filtrar.
    /// <para>
    /// Nota de alcance: esto NO es el estado compuesto "Firmado" de la columna del listado (que
    /// además considera identidad aprobada y firma de baúl — ver <c>FirmaParteEstados</c>). Ese estado
    /// compuesto no vive en una sola columna/tabla consultable por SQL directo sin replicar en el
    /// motor la misma lógica de <c>ListProcedureInstancesHandler.DeriveFirmaParte</c>, lo que excede el
    /// alcance de esta migración. El filtro aquí es sobre <c>procedure_instance_signatures</c>
    /// (firma electrónica de la compraventa), que sí es consultable directo con EXISTS/NOT EXISTS.
    /// </para>
    /// </summary>
    public bool? Firmado { get; init; }

    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedTo { get; init; }

    /// <summary><c>true</c> si algún criterio está activo (evita armar WHERE de más en el caso común sin filtros).</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Vin) || !string.IsNullOrWhiteSpace(Placa)
        || !string.IsNullOrWhiteSpace(Vendedor) || !string.IsNullOrWhiteSpace(Comprador)
        || !string.IsNullOrWhiteSpace(Gestor) || Firmado is not null
        || CreatedFrom is not null || CreatedTo is not null
        || UpdatedFrom is not null || UpdatedTo is not null;
}
