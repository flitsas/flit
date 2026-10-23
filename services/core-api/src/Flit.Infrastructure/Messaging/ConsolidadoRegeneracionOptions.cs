namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12795 — opciones de la cola de regeneración anticipada de consolidados. Sección
/// <c>ConsolidadoRegeneracion</c> de appsettings (todas opcionales; los defaults sirven en los tres
/// ambientes).
/// </summary>
public sealed class ConsolidadoRegeneracionOptions
{
    public const string SectionName = "ConsolidadoRegeneracion";

    /// <summary>
    /// Ventana de debounce: una solicitud se ejecuta cuando pasa este tiempo SIN que llegue otra de la
    /// misma clave (tenant, trámite, documento). Cada solicitud nueva reinicia la ventana.
    /// </summary>
    public TimeSpan VentanaDebounce { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tope a los reinicios de la ventana, contado desde la PRIMERA solicitud de la clave: un trámite
    /// que recibe solicitudes sin pausa se regenera igual a lo sumo cada este tiempo, en vez de
    /// posponerse indefinidamente.
    /// </summary>
    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Capacidad del canal en memoria. Lleno, <c>Encolar</c> devuelve <c>false</c> sin bloquear: la
    /// regeneración anticipada es best-effort y el camino perezoso cubre lo descartado.
    /// </summary>
    public int Capacidad { get; set; } = 1_024;
}
