namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Si un error de una fuente se puede reintentar (D-12, HU #13737).</summary>
public enum ClaseError
{
    /// <summary>Se puede resolver solo (Timeout, Red, HTTP 5xx): se reintenta con backoff.</summary>
    Transitorio,

    /// <summary>No se resuelve reintentando: la celda queda en ERROR reprocesable sin gastar más consumo.</summary>
    Definitivo,
}

/// <summary>
/// Clasificación de los errores de una fuente por su código (<c>tipo_error</c>, HU #13737, F03-H6, D-12). Manda lo que devuelve
/// core-consultas: <c>TIMEOUT</c>, <c>RED</c> y <c>HTTP_5XX</c> son transitorios; <c>RESPUESTA_INVALIDA</c>,
/// <c>AUTENTICACION</c>, <c>ESTADO_NO_OK</c> y <c>NO_CONECTADO</c> son definitivos. Todo lo demás (un código que no está en la
/// tabla, <c>BLOQUEADA_POR_RUNT</c>, un fallo sin código) es definitivo: no se gasta consumo en lo que no se sabe si se resuelve
/// solo. Regla pura, sin estado.
/// </summary>
/// <example>
/// <code>
/// if (ClasificadorErrores.Clasificar(resultado.TipoError) == ClaseError.Transitorio) { /* reintentar con backoff */ }
/// </code>
/// </example>
public static class ClasificadorErrores
{
    private static readonly HashSet<string> Transitorios = new(StringComparer.Ordinal) { "TIMEOUT", "RED", "HTTP_5XX" };

    private static readonly HashSet<string> Definitivos = new(StringComparer.Ordinal)
    {
        "RESPUESTA_INVALIDA", "AUTENTICACION", "ESTADO_NO_OK", "NO_CONECTADO",
    };

    /// <summary>Clase del error con el código dado; sin código o con uno desconocido, <see cref="ClaseError.Definitivo"/>.</summary>
    public static ClaseError Clasificar(string? tipoError) =>
        tipoError is not null && Transitorios.Contains(tipoError) ? ClaseError.Transitorio : ClaseError.Definitivo;

    /// <summary>Verdadero si el código está en la tabla (transitorio o definitivo declarado).</summary>
    public static bool EsConocido(string? tipoError) =>
        tipoError is not null && (Transitorios.Contains(tipoError) || Definitivos.Contains(tipoError));
}
