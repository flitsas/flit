using Flit.Queries.Domain;

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

    /// <summary>
    /// Condiciones armadas con la gramática de Consultas (HU #12106): campo del catálogo, operador y
    /// valores. Es la vía por la que crece el filtro de aquí en adelante — cada campo nuevo se declara
    /// en <c>TramitesQueryFieldCatalog</c> y se traduce en el repositorio, sin añadir una propiedad
    /// más a este record ni un parámetro más al endpoint.
    /// <para>
    /// Convive con los campos sueltos de arriba en vez de reemplazarlos: los siguen mandando el
    /// listado actual y el conteo por estado, y romper ese contrato obligaría a desplegar frontend y
    /// backend a la vez. Cuando la barra de filtros migre del todo, los de arriba se podrán retirar.
    /// </para>
    /// <para>
    /// Llegan YA VALIDADAS contra el catálogo: un campo o un operador desconocido se rechaza en el
    /// endpoint con 400. El repositorio nunca ve un identificador que no conozca, y en ningún caso el
    /// texto del cliente se concatena en SQL — solo viaja como parámetro.
    /// </para>
    /// </summary>
    public IReadOnlyList<QueryCondition>? Condiciones { get; init; }

    /// <summary>
    /// HU #12187 — búsqueda de texto libre del listado, transversal a varios campos.
    ///
    /// <para><b>Por qué es un filtro del servidor y no del cliente.</b> Este cruce se hacía en el
    /// navegador sobre las filas ya traídas, y el listado trae como mucho una página: buscar un
    /// trámite que existe pero quedó fuera respondía «sin resultados». No es una limitación que el
    /// gestor pueda ver —la pantalla no dice sobre cuántas filas buscó—, así que la respuesta era
    /// sencillamente falsa. Y los contadores de estado, que SÍ salen del servidor, seguían
    /// reportando el universo entero: la pantalla se contradecía sola.</para>
    ///
    /// <para><b>El radicado casa EXACTO; el resto, por subcadena.</b> Desde el Feature #12150 el
    /// radicado es un consecutivo numérico corto, así que por subcadena buscar <c>1</c> traería el
    /// 1, el 10, el 11 y el 100 — todo el listado con la apariencia de un resultado. Exacto es
    /// además lo que quiere quien teclea un radicado: ese trámite, no los que lo contienen.</para>
    /// </summary>
    public string? Busqueda { get; init; }

    /// <summary>
    /// HU #12187 — solo los trámites marcados como prioritarios (<c>true</c>), o sin filtrar
    /// (<c>null</c>).
    ///
    /// <para>Sube al servidor por la misma razón que la búsqueda, y con una urgencia mayor: en
    /// cuanto el listado pagine de verdad (HU #12188), un filtro aplicado en el cliente dejaría de
    /// mirar la ventana de 200 para mirar solo las diez filas de la página a la vista. Habría
    /// empeorado en vez de quedarse igual.</para>
    /// </summary>
    public bool? Prioritario { get; init; }

    /// <summary><c>true</c> si algún criterio está activo (evita armar WHERE de más en el caso común sin filtros).</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Vin) || !string.IsNullOrWhiteSpace(Placa)
        || !string.IsNullOrWhiteSpace(Vendedor) || !string.IsNullOrWhiteSpace(Comprador)
        || !string.IsNullOrWhiteSpace(Gestor) || Firmado is not null
        || CreatedFrom is not null || CreatedTo is not null
        || UpdatedFrom is not null || UpdatedTo is not null
        || Estados is { Count: > 0 } || !string.IsNullOrWhiteSpace(Modalidad)
        || !string.IsNullOrWhiteSpace(OrganismoTransito) || !string.IsNullOrWhiteSpace(TipoCodigo)
        || !string.IsNullOrWhiteSpace(Busqueda) || Prioritario is not null
        || Condiciones is { Count: > 0 };
}
