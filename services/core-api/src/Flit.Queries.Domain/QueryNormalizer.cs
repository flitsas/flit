namespace Flit.Queries.Domain;

/// <summary>
/// Deja lo que llega de la red en forma canónica.
///
/// <para>Lo que manda el cliente es una PROPUESTA de consulta, no una definición. Un campo que no
/// existe, un operador que ese campo no admite o una lista de mil placas se recortan a algo válido
/// antes de tocar la base. Se normaliza en el borde y otra vez en el repositorio: la segunda no
/// sobra, porque las consultas guardadas se leen de la base y pudieron guardarse con un catálogo
/// anterior.</para>
/// </summary>
public static class QueryNormalizer
{
    /// <summary>
    /// Deja la condición en forma canónica o la descarta.
    ///
    /// <para>Descartar es lo correcto y no un atajo: una condición sin valores no restringe nada, y
    /// tratarla como error rompería una consulta guardada mientras el usuario la está editando —
    /// justo cuando acaba de borrar el último valor para escribir otro.</para>
    /// </summary>
    public static QueryCondition? Normalize(IQueryFieldCatalog catalog, QueryCondition? condition)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (condition is null || !QueryOperator.IsKnown(condition.Operator))
        {
            return null;
        }

        var field = catalog.Find(condition.FieldId);
        if (field is null || !field.Operators.Contains(condition.Operator, StringComparer.Ordinal))
        {
            return null;
        }

        if (QueryOperator.IsUnary(condition.Operator))
        {
            return new QueryCondition(field.Id, condition.Operator, []);
        }

        var values = (condition.Values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(QueryLimits.MaxValoresPorCondicion)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        // «Contiene» es un solo texto por definición; si llegan varios se queda con el primero en
        // vez de rechazar la consulta entera.
        if (condition.Operator == QueryOperator.Contiene && values.Count > 1)
        {
            values = [values[0]];
        }

        return new QueryCondition(field.Id, condition.Operator, values);
    }

    /// <summary>
    /// Deja la definición completa en forma canónica: condiciones válidas, fechas conocidas y orden
    /// de la lista cerrada. Lo que no se reconoce se cae en silencio, por la misma razón que en
    /// <see cref="Normalize(IQueryFieldCatalog, QueryCondition)"/>.
    /// </summary>
    public static QueryDefinition Normalize(IQueryFieldCatalog catalog, QueryDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var fechas = definition?.Fechas;
        var campo = catalog.IsKnownDateField(fechas?.Campo) ? fechas!.Campo : catalog.DefaultDateField;
        var preset = QueryRangePreset.IsKnown(fechas?.Preset) ? fechas!.Preset : QueryRangePreset.Ultimos30;

        var condiciones = (definition?.Condiciones ?? [])
            .Select(c => Normalize(catalog, c))
            .Where(c => c is not null)
            .Select(c => c!)
            // Una condición por campo y operador: dos «placa es alguno» seguidas serían un «y» de
            // dos listas, que nunca es lo que el usuario quiso decir al agregar la segunda.
            .GroupBy(c => (c.FieldId, c.Operator))
            .Select(g => g.Last())
            .Take(QueryLimits.MaxCondiciones)
            .ToList();

        return new QueryDefinition(
            new QueryDateFilter(campo, preset, fechas?.From, fechas?.To),
            condiciones,
            definition?.Columnas ?? [],
            catalog.IsKnownSort(definition?.SortBy) ? definition!.SortBy : catalog.DefaultSort,
            definition?.Descending ?? true);
    }

    /// <summary>Recorta la página pedida a algo servible.</summary>
    public static QueryRequest BuildRequest(
        IQueryFieldCatalog catalog, QueryDefinition? definition, int? page, int? pageSize) =>
        new(
            Normalize(catalog, definition),
            Math.Max(1, page ?? 1),
            Math.Clamp(pageSize ?? QueryLimits.DefaultPageSize, 1, QueryLimits.MaxPageSize));
}
