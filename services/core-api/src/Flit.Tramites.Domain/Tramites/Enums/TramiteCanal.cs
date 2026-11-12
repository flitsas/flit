namespace Flit.Tramites.Domain.Tramites.Enums;

/// <summary>
/// HU #14317 (Feature #14315, Épica #14296) — canal por el que entró el trámite. Es la fuente de códigos y
/// nombres visibles en el backend; los mismos cinco códigos viven en el CHECK
/// <c>ck_procedure_instances_channel</c> (172-HU14317) y en el enum <c>TramiteCanal</c> de
/// <c>contracts/openapi/core-api.v1.yaml</c> (una prueba los compara).
/// <para>
/// <b>Aplicación</b> está en el catálogo pero hoy ningún flujo la asigna. Una fila sin canal (<c>null</c>)
/// es un trámite histórico y se lee como Trámites (<see cref="Efectivo"/>).
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

    /// <summary>Valor del campo <c>datos.canal</c> de la solicitud del portal que marca el punto de atención de un OT.</summary>
    public const string CanalSolicitudPunto = "pn_punto";

    /// <summary>
    /// Canal con el que se lee un trámite: el persistido, o Trámites cuando no hay dato (histórico o
    /// migrado de V1). Única regla de «sin dato»; el detalle (HU #14318) y los reportes (HU #14319) la comparten.
    /// </summary>
    public static string Efectivo(string? codigo) => string.IsNullOrWhiteSpace(codigo) ? Tramites : codigo;

    /// <summary>
    /// Canal de un trámite que llega del portal Persona Natural (HU #14318): <c>pn_punto</c> cuando la
    /// solicitud trae ese canal (sin distinguir mayúsculas), <c>pn_web</c> en cualquier otro caso, incluido
    /// el campo ausente.
    /// </summary>
    public static string DesdeSolicitudPortal(string? canalSolicitud) =>
        string.Equals(canalSolicitud, CanalSolicitudPunto, StringComparison.OrdinalIgnoreCase)
            ? PersonaNaturalOt
            : PersonaNaturalWeb;

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
