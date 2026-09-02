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

    /// <summary>
    /// Estados del ciclo de vida a incluir (OR entre ellos). Vacío/null = todos.
    /// <para>
    /// Es una LISTA y no un solo valor porque la pregunta natural del gestor es "todo lo que no está
    /// cerrado", que son varios estados a la vez. Filtrar por estado dejó de hacerse en el cliente:
    /// el listado devuelve como mucho <c>MaxItems</c> filas, así que un filtro aplicado sobre lo ya
    /// traído no respondía "los borradores del tenant" sino "los borradores que cupieron en la
    /// ventana", que es una respuesta distinta y silenciosamente incompleta.
    /// </para>
    /// </summary>
    public IReadOnlyList<string>? Estados { get; init; }

    /// <summary>Familia del trámite (código de <c>ProcedureFamilyCodes</c>). Misma razón que
    /// <see cref="Estados"/>: la pestaña de familia filtraba sobre la ventana ya traída.</summary>
    public string? Modalidad { get; init; }

    /// <summary>
    /// Organismo de tránsito por SUBCADENA sobre el nombre elegido en el trámite. El nombre no es una
    /// columna de la instancia: vive como <c>field_value</c> <c>transit_office_name</c> (lo mismo que
    /// proyecta el listado), así que el filtro va por <c>EXISTS</c> sobre los valores del expediente.
    /// </summary>
    public string? OrganismoTransito { get; init; }

    /// <summary>
    /// Código del TIPO de trámite (no la familia). La familia "OTROS" agrupa quince tipos con
    /// recorridos distintos —blindaje, cambio de color, levantamiento de prenda…— que sin esto solo
    /// se podían pedir todos juntos. Igualdad exacta case-insensitive.
    /// </summary>
    public string? TipoCodigo { get; init; }

    /// <summary><c>true</c> si algún criterio está activo (evita armar WHERE de más en el caso común sin filtros).</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Vin) || !string.IsNullOrWhiteSpace(Placa)
        || !string.IsNullOrWhiteSpace(Vendedor) || !string.IsNullOrWhiteSpace(Comprador)
        || !string.IsNullOrWhiteSpace(Gestor) || Firmado is not null
        || CreatedFrom is not null || CreatedTo is not null
        || UpdatedFrom is not null || UpdatedTo is not null
        || Estados is { Count: > 0 } || !string.IsNullOrWhiteSpace(Modalidad)
        || !string.IsNullOrWhiteSpace(OrganismoTransito) || !string.IsNullOrWhiteSpace(TipoCodigo);
}
