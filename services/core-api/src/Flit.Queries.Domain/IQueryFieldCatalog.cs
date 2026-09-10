namespace Flit.Queries.Domain;

/// <summary>
/// Qué se puede preguntar en un módulo concreto: los campos, sobre qué fechas se filtra y por qué
/// columnas se ordena.
///
/// <para>Es la única pieza que cada módulo escribe entera. El organismo pregunta por «empresa
/// cliente» y por el estado leído desde su bandeja; la empresa gestora pregunta por «organismo de
/// tránsito» y por el estado del trámite en su propio ciclo. Todo lo demás —operadores, rangos,
/// normalización, cobertura— es el motor y no se reescribe.</para>
/// </summary>
public interface IQueryFieldCatalog
{
    /// <summary>Campos consultables, en el orden en que se ofrecen.</summary>
    IReadOnlyList<QueryFieldDto> Fields { get; }

    /// <summary>Fechas sobre las que puede aplicarse el rango.</summary>
    IReadOnlyList<QueryFieldOptionDto> DateFields { get; }

    /// <summary>La fecha que se usa si la consulta no dice otra cosa.</summary>
    string DefaultDateField { get; }

    /// <summary>Órdenes admitidos. Lista cerrada: un campo libre sería inyección de orden.</summary>
    IReadOnlyList<string> SortFields { get; }

    string DefaultSort { get; }

    /// <summary>
    /// Cómo se nombra, en una frase, el conjunto al que llega quien consulta («el organismo», «su
    /// empresa»). Sale en el aviso de cobertura, que es donde la diferencia importa: decirle a un
    /// gestor que una placa «no está en el organismo» cuando lo que pasa es que no es suya sería
    /// mandarlo a preguntar al sitio equivocado.
    /// </summary>
    string Universo { get; }

    /// <summary>
    /// Campos por los que tiene sentido rendir cuentas uno a uno en el aviso de cobertura. Son los
    /// que el usuario escribe o pega esperando ver cada valor de vuelta; avisar de que «traspaso» no
    /// salió en un filtro por tipo de trámite sería ruido, no información.
    /// </summary>
    bool IsIdentifier(string fieldId);

    QueryFieldDto? Find(string? fieldId) =>
        fieldId is null ? null : Fields.FirstOrDefault(f => f.Id == fieldId);

    bool IsKnownField(string? fieldId) => Find(fieldId) is not null;

    /// <summary>Etiqueta legible de un campo, para el aviso de cobertura y los mensajes de error.</summary>
    string LabelOf(string fieldId) => Find(fieldId)?.Label ?? fieldId;

    bool IsKnownDateField(string? campo) =>
        campo is not null && DateFields.Any(d => d.Value == campo);

    string DateFieldLabel(string campo) =>
        DateFields.FirstOrDefault(d => d.Value == campo)?.Label ?? "la fecha";

    bool IsKnownSort(string? sortBy) =>
        sortBy is not null && SortFields.Contains(sortBy, StringComparer.Ordinal);
}
