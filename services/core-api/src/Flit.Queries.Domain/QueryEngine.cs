namespace Flit.Queries.Domain;

/// <summary>
/// Evalúa una consulta sobre filas ya materializadas: construye los predicados, aplica el rango de
/// fechas y arma el aviso de cobertura.
///
/// <para><b>Cada campo se reduce a la MISMA forma</b>: la lista de textos por los que esa fila puede
/// coincidir. Un booleano devuelve <c>["true"]</c>, un comprador devuelve su nombre y su documento,
/// un trámite sin transformaciones devuelve la lista vacía. Reducirlo todo a esa forma es lo que
/// deja los operadores en cinco y hace que «no es ninguna» funcione igual sobre una empresa que
/// sobre las transformaciones — sin un caso especial por campo, que es donde estos motores acumulan
/// incoherencias.</para>
///
/// <para>El módulo que lo usa solo aporta tres cosas: el catálogo, cómo se accede a cada campo de su
/// fila y de dónde sale cada fecha.</para>
/// </summary>
/// <typeparam name="TRow">La fila materializada del módulo, con todo lo que una condición pueda preguntarle.</typeparam>
public sealed class QueryEngine<TRow>
    where TRow : class
{
    private readonly IQueryFieldCatalog _catalog;
    private readonly Func<string, Func<TRow, IReadOnlyList<string>>> _accessors;
    private readonly Func<TRow, string, DateTimeOffset?> _dateOf;

    /// <param name="catalog">Qué se puede preguntar en este módulo.</param>
    /// <param name="accessors">Dado un id de campo, cómo se saca de una fila la lista de textos con los que puede coincidir.</param>
    /// <param name="dateOf">Dada una fila y el id de una fecha del catálogo, qué instante le corresponde.</param>
    public QueryEngine(
        IQueryFieldCatalog catalog,
        Func<string, Func<TRow, IReadOnlyList<string>>> accessors,
        Func<TRow, string, DateTimeOffset?> dateOf)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _accessors = accessors ?? throw new ArgumentNullException(nameof(accessors));
        _dateOf = dateOf ?? throw new ArgumentNullException(nameof(dateOf));
    }

    /// <summary>
    /// Cómo se compara cada campo. Los identificadores ignoran guiones, puntos y espacios porque una
    /// lista pegada desde Excel trae «ABC-123» tan a menudo como «ABC123», y que la consulta dependa
    /// de eso convertiría el aviso de cobertura en una lista de falsos «no existe».
    ///
    /// <para>Esta regla se escribe DOS veces —aquí y como expresión sobre el motor de base de datos,
    /// en el repositorio que empuja el filtro por identificador— y las dos tienen que decir lo
    /// mismo. Si se cambia una hay que cambiar la otra: una consulta que filtra distinto según dónde
    /// se evalúe pierde filas en silencio.</para>
    /// </summary>
    public Func<string, string> NormalizerFor(string fieldId) =>
        _catalog.IsIdentifier(fieldId) ? SinSeparadores : EnMayusculas;

    /// <summary>Quita los separadores con los que la gente escribe placas y radicados, y sube a mayúsculas.</summary>
    public static string SinSeparadores(string value) => (value ?? string.Empty)
        .ToUpperInvariant()
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal);

    private static string EnMayusculas(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public Func<TRow, bool> BuildPredicate(QueryCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var accessor = _accessors(condition.FieldId);
        var normalize = NormalizerFor(condition.FieldId);
        var objetivos = condition.Values.Select(normalize).ToHashSet(StringComparer.Ordinal);

        return condition.Operator switch
        {
            QueryOperator.EsAlguno => row =>
                accessor(row).Any(v => objetivos.Contains(normalize(v))),
            QueryOperator.NoEsNinguno => row =>
                !accessor(row).Any(v => objetivos.Contains(normalize(v))),
            QueryOperator.Contiene => row =>
                accessor(row).Any(v => normalize(v).Contains(objetivos.First(), StringComparison.Ordinal)),
            QueryOperator.EstaVacio => row =>
                accessor(row).All(string.IsNullOrWhiteSpace),
            QueryOperator.NoEstaVacio => row =>
                accessor(row).Any(v => !string.IsNullOrWhiteSpace(v)),
            _ => _ => true,
        };
    }

    /// <summary>
    /// ¿Cae la fila en el rango, según la fecha elegida?
    ///
    /// <para>Sin esa fecha, NO cae. Un trámite sin decidir no aparece en una consulta filtrada por
    /// fecha de decisión, y es lo correcto: la pregunta era qué se decidió en esas fechas. La
    /// alternativa —colarlos— haría que el mismo filtro significara cosas distintas según la fila.</para>
    /// </summary>
    public bool InRange(TRow row, string campo, DateTimeOffset from, DateTimeOffset to)
    {
        var fecha = _dateOf(row, campo);
        return fecha is not null && fecha >= from && fecha <= to;
    }

    /// <summary>
    /// Qué pasó con cada valor que el usuario pidió por nombre.
    ///
    /// <para>Es el requisito que hace que el resultado se pueda leer sin desconfiar. Si alguien pega
    /// dos placas, marca «tiene LT» y le sale una fila, la pregunta inmediata es si se perdió un
    /// dato. Aquí la respuesta viene con el resultado: o la otra no existe a su alcance, o existe y
    /// la dejó fuera exactamente esta condición.</para>
    ///
    /// <para>Se evalúan las condiciones una a una y en el orden en que el usuario las escribió, y se
    /// reporta la PRIMERA que falla. Reportar todas las que fallan sería más completo y menos útil:
    /// lo accionable es el filtro que hay que aflojar primero.</para>
    /// </summary>
    public IReadOnlyList<QueryCoverageItemDto> BuildCoverage(
        QueryDefinition definition,
        IReadOnlyCollection<TRow> universo,
        IReadOnlyCollection<TRow> matched,
        IReadOnlyCollection<(QueryCondition Condition, Func<TRow, bool> Predicate)> condiciones,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(universo);
        ArgumentNullException.ThrowIfNull(matched);
        ArgumentNullException.ThrowIfNull(condiciones);

        var listados = definition.Condiciones
            .Where(c => c.Operator == QueryOperator.EsAlguno && _catalog.IsIdentifier(c.FieldId))
            .ToList();

        if (listados.Count == 0)
        {
            return [];
        }

        var items = new List<QueryCoverageItemDto>();

        foreach (var condicion in listados)
        {
            var accessor = _accessors(condicion.FieldId);
            var normalizer = NormalizerFor(condicion.FieldId);

            foreach (var pedido in condicion.Values)
            {
                var objetivo = normalizer(pedido);

                bool Coincide(TRow row) => accessor(row).Any(v => normalizer(v) == objetivo);

                if (matched.Any(Coincide))
                {
                    items.Add(new QueryCoverageItemDto(
                        condicion.FieldId, pedido, QueryCoverageResult.Encontrado, null, null));
                    continue;
                }

                var candidato = universo.FirstOrDefault(Coincide);
                if (candidato is null)
                {
                    items.Add(new QueryCoverageItemDto(
                        condicion.FieldId,
                        pedido,
                        QueryCoverageResult.NoExiste,
                        null,
                        $"No hay ningún trámite con este valor en {_catalog.Universo}."));
                    continue;
                }

                // Existe: alguna condición lo dejó fuera. La fecha se mira primero porque es la que
                // el usuario tiene menos presente — está en la barra de arriba, no entre los chips.
                if (!InRange(candidato, definition.Fechas.Campo, from, to))
                {
                    var etiqueta = _catalog.DateFieldLabel(definition.Fechas.Campo);

                    items.Add(new QueryCoverageItemDto(
                        condicion.FieldId,
                        pedido,
                        QueryCoverageResult.Excluido,
                        definition.Fechas.Campo,
                        $"Existe, pero su {etiqueta.ToLowerInvariant()} queda fuera del rango."));
                    continue;
                }

                var culpable = condiciones
                    .Where(c => c.Condition.FieldId != condicion.FieldId)
                    .FirstOrDefault(c => !c.Predicate(candidato));

                items.Add(new QueryCoverageItemDto(
                    condicion.FieldId,
                    pedido,
                    QueryCoverageResult.Excluido,
                    culpable.Condition?.FieldId,
                    culpable.Condition is null
                        // Puede pasar si el mismo valor aparece en varias filas y ninguna pasa por
                        // razones distintas; decir «otro filtro» es más honesto que señalar uno al azar.
                        ? "Existe, pero ningún trámite con este valor cumple todos los filtros."
                        : $"Existe, pero lo dejó fuera el filtro «{_catalog.LabelOf(culpable.Condition.FieldId)}»."));
            }
        }

        return items;
    }
}
