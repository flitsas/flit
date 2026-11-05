namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// HU #14317 (Feature #14315, Épica #14296) — canal por el que entró el trámite. Es la fuente de códigos y
/// nombres visibles en el backend; los mismos cinco códigos viven en el CHECK
/// <c>ck_procedure_instances_channel</c> (169-HU14317) y en el enum <c>TramiteCanal</c> de
/// <c>contracts/openapi/core-api.v1.yaml</c> (una prueba los compara).
/// <para>
/// <b>Aplicación</b> está en el catálogo pero hoy ningún flujo la asigna. Una fila sin canal (<c>null</c>)
/// es un trámite histórico y se lee como Trámites en los reportes (decisión de las HUs siguientes).
/// </para>
/// </summary>
public static class TramiteCanal
{
    /// <summary>Lo crea el gestor en el producto Trámites.</summary>
    public const string Tramites = "tramites";

    /// <summary>Llega del portal Persona Natural.</summary>
    public const string PersonaNaturalWeb = "pn_web";

    /// <summary>Llega del punto de atención de un organismo de tránsito; exige el OT del canal.</summary>
    public const string PersonaNaturalOt = "pn_punto";

    /// <summary>Lo crea la integración con terceros (ICT).</summary>
    public const string Integracion = "integracion";

    /// <summary>Reservado; hoy no se asigna.</summary>
    public const string Aplicacion = "aplicacion";

    /// <summary>Los cinco códigos del catálogo, en orden de presentación.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        Tramites,
        PersonaNaturalWeb,
        PersonaNaturalOt,
        Integracion,
        Aplicacion,
    ];

    /// <summary>¿El código pertenece al catálogo? Comparación exacta (los códigos se persisten en minúscula).</summary>
    public static bool EsValido(string? codigo) =>
        codigo is not null && Todos.Contains(codigo, StringComparer.Ordinal);

    /// <summary>Canal efectivo: un trámite sin canal (<c>null</c>, histórico) se lee como Trámites.</summary>
    public static string Efectivo(string? codigo) => codigo ?? Tramites;

    /// <summary>Nombre visible del canal, o <c>null</c> si el código no pertenece al catálogo.</summary>
    public static string? NombreVisible(string? codigo) => codigo switch
    {
        Tramites => "Trámites",
        PersonaNaturalWeb => "Persona Natural Web",
        PersonaNaturalOt => "Persona Natural OT",
        Integracion => "Integración",
        Aplicacion => "Aplicación",
        _ => null,
    };
}
