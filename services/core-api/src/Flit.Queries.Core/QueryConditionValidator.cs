namespace Flit.Queries.Domain;

/// <summary>
/// Valida las condiciones de un filtro contra SU catálogo, antes de que lleguen al repositorio.
///
/// <para>Es lo que impide que un campo o un operador que no está en el catálogo se cuele: se rechaza
/// con un mensaje que lo nombra, en vez de ignorarse. Ignorarlo sería lo peligroso — devolvería un
/// listado MÁS AMPLIO del que el usuario pidió con la apariencia de estar filtrado, y nadie revisa
/// un resultado que parece correcto.</para>
///
/// <para>Vive junto al motor y no junto a un catálogo porque las reglas no dependen de qué se
/// pregunta: un operador que el campo no admite, una lista pegada en un campo que solo acepta un
/// valor o una opción inexistente son errores igual en el listado del gestor que en la bandeja del
/// organismo. Lo único que cambia es CÓMO se llama la superficie en el mensaje, y eso entra por
/// parámetro.</para>
///
/// <para>El texto del cliente no llega nunca a la consulta: lo único que se acepta son
/// identificadores de una lista cerrada, y los valores viajan como parámetros.</para>
/// </summary>
public static class QueryConditionValidator
{
    /// <summary>Mensaje del primer problema encontrado, o <c>null</c> si todas son válidas.</summary>
    /// <param name="catalogo">Qué se puede preguntar en esta superficie.</param>
    /// <param name="condiciones">Las condiciones recibidas del cliente.</param>
    /// <param name="superficie">
    /// Cómo nombrar la pantalla en el mensaje de un campo desconocido («el listado de trámites», «la
    /// bandeja del organismo»). Es lo único que distingue a un consumidor de otro.
    /// </param>
    public static string? Validate(
        IQueryFieldCatalog catalogo,
        IReadOnlyList<QueryCondition>? condiciones,
        string superficie)
    {
        ArgumentNullException.ThrowIfNull(catalogo);

        if (condiciones is null || condiciones.Count == 0)
            return null;

        foreach (var condicion in condiciones)
        {
            var campo = catalogo.Find(condicion.FieldId);
            if (campo is null)
                return $"El campo «{condicion.FieldId}» no se puede filtrar en {superficie}.";

            if (!QueryOperator.IsKnown(condicion.Operator))
                return $"El operador «{condicion.Operator}» no existe.";

            if (!campo.Operators.Contains(condicion.Operator, StringComparer.Ordinal))
                return $"El campo «{campo.Label}» no admite el operador «{condicion.Operator}».";

            var unario = QueryOperator.IsUnary(condicion.Operator);
            var valores = condicion.Values ?? [];

            if (unario && valores.Count > 0)
                return $"El operador «{condicion.Operator}» de «{campo.Label}» no lleva valores.";

            if (!unario && valores.Count == 0)
                return $"Falta el valor del filtro «{campo.Label}».";

            // «Contiene» compara UN texto: con varios, el resultado dependería de cuál se eligiera.
            if (condicion.Operator == QueryOperator.Contiene && valores.Count > 1)
                return $"El filtro «{campo.Label}» con «contiene» admite un solo valor.";

            // Una lista pegada en un campo que no la admite suele ser un error de quien la pega, y
            // aceptarla en silencio daría un resultado que no se corresponde con lo que ve en pantalla.
            if (!campo.AdmiteLista && valores.Count > 1)
                return $"El filtro «{campo.Label}» admite un solo valor.";

            // Una opción fuera del catálogo solo puede devolver cero: decirlo es más útil que
            // devolver una lista vacía sin explicación.
            if (campo.Options.Count > 0 && !unario)
            {
                var desconocida = valores.FirstOrDefault(v =>
                    !campo.Options.Any(o => string.Equals(o.Value, v, StringComparison.OrdinalIgnoreCase)));
                if (desconocida is not null)
                    return $"«{desconocida}» no es una opción de «{campo.Label}».";
            }
        }

        return null;
    }
}
