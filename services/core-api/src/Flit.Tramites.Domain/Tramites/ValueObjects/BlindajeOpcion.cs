namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>Opción declarada en un trámite <c>BLINDAJE</c>: el nivel que se instala, o el desmonte.</summary>
public enum BlindajeOpcion
{
    /// <summary>Nada declarado todavía (o valor no reconocido).</summary>
    Ninguna,
    Nivel1,
    Nivel2,
    Nivel3,

    /// <summary>Se retira el blindaje: el vehículo deja de estar blindado.</summary>
    Desmonte,
}

/// <summary>
/// Fuente única de la opción de blindaje, compartida por el asistente (qué se ofrece), el FUR (qué
/// casilla marca y qué imprime en observaciones) y los tests de ambos lados.
///
/// <para><b>Por qué existe.</b> Hasta ahora un trámite de blindaje solo persistía la bandera
/// <c>blindaje = true</c>: un SÍ/NO que no distinguía el nivel instalado ni admitía el desmonte, de
/// modo que el organismo recibía tres trámites distintos con el mismo formulario. La opción es el
/// dato que el FUR declara; la bandera pasa a ser un derivado suyo
/// (<see cref="DejaElVehiculoBlindado"/>), no un dato independiente que pudiera contradecirla.</para>
///
/// <para>El desmonte es la razón de que la bandera se derive y no se copie: es la única opción que
/// deja el vehículo SIN blindaje, así que la casilla del formulario debe salir en <c>NO</c> aunque el
/// trámite sea, precisamente, un blindaje.</para>
/// </summary>
public static class BlindajeOpciones
{
    /// <summary>Clave de <c>field_values</c> donde el asistente persiste la opción.</summary>
    public const string FieldKey = "blindaje_nivel";

    /// <summary>Bandera derivada (<c>field_values</c>) que el resto del expediente ya consumía.</summary>
    public const string BanderaFieldKey = "blindaje";

    public const string CodigoNivel1 = "NIVEL_1";
    public const string CodigoNivel2 = "NIVEL_2";
    public const string CodigoNivel3 = "NIVEL_3";
    public const string CodigoDesmonte = "DESMONTE";

    /// <summary>Códigos admitidos, en el orden en que se le ofrecen al gestor.</summary>
    public static readonly IReadOnlyList<string> Codigos =
        [CodigoNivel1, CodigoNivel2, CodigoNivel3, CodigoDesmonte];

    /// <summary>
    /// Lee la opción persistida. Un valor ausente, vacío o no reconocido devuelve
    /// <see cref="BlindajeOpcion.Ninguna"/>: no se adivina un nivel a partir de un dato roto, porque
    /// el nivel es lo que el formulario declara ante el organismo.
    /// </summary>
    public static BlindajeOpcion Parse(string? valor) =>
        (valor ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            CodigoNivel1 => BlindajeOpcion.Nivel1,
            CodigoNivel2 => BlindajeOpcion.Nivel2,
            CodigoNivel3 => BlindajeOpcion.Nivel3,
            CodigoDesmonte => BlindajeOpcion.Desmonte,
            _ => BlindajeOpcion.Ninguna,
        };

    /// <summary>Código canónico de una opción, o <c>null</c> para <see cref="BlindajeOpcion.Ninguna"/>.</summary>
    public static string? ToCodigo(BlindajeOpcion opcion) => opcion switch
    {
        BlindajeOpcion.Nivel1 => CodigoNivel1,
        BlindajeOpcion.Nivel2 => CodigoNivel2,
        BlindajeOpcion.Nivel3 => CodigoNivel3,
        BlindajeOpcion.Desmonte => CodigoDesmonte,
        _ => null,
    };

    /// <summary>
    /// ¿El vehículo queda blindado al terminar el trámite? Solo los tres niveles; el desmonte no.
    /// De aquí sale la casilla «vehículo blindado SI/NO» del FUR.
    /// </summary>
    public static bool DejaElVehiculoBlindado(BlindajeOpcion opcion) =>
        opcion is BlindajeOpcion.Nivel1 or BlindajeOpcion.Nivel2 or BlindajeOpcion.Nivel3;
}
