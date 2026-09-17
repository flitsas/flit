namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Quién pide una transición de estado (ADR-0059). Solo las aristas de la RUTA DE PLACA discriminan por
/// actor: la cola de placa la mueve el Organismo de Tránsito y el envío al OT lo hace el gestor. Las
/// aristas previas a ADR-0059 no miran el actor (sus autorizaciones viven en los endpoints).
/// </summary>
public enum TramiteActor
{
    /// <summary>Usuario de la empresa cliente (radica, subsana, envía al OT).</summary>
    Gestor,

    /// <summary>Organismo de Tránsito (asigna, libera, rechaza, decide).</summary>
    Ot,

    /// <summary>
    /// Integración Quipux (ADR-0051): registra el documento sobre <c>preparado</c> y entrega en un solo
    /// salto, pida placa el tipo o no. Nunca pasa por la ruta de placa.
    /// </summary>
    Quipux,

    /// <summary>Proceso automático de la plataforma actuando en nombre de la empresa (p. ej. radicación automática).</summary>
    Sistema,
}
