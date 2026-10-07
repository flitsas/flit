namespace Flit.Modules.Notificaciones.Webhooks;

/// <summary>
/// HU #13356 — datos del trabajo <see cref="Tipo"/>: un webhook saliente YA ARMADO Y FIRMADO por el servicio dueño de la
/// suscripción (mismo criterio que los correos, Feature #13324): la llave de firma no sale de ese servicio. Notificaciones
/// lo entrega tal cual (cuerpo y cabeceras), con el filtro de destinos internos, reintentos y mensajes muertos.
/// </summary>
/// <param name="Url">Destino de la suscripción.</param>
/// <param name="Cuerpo">JSON exacto que se firmó (la firma es del cuerpo: no se re-serializa).</param>
/// <param name="Cabeceras">Firma, correlación y lo que el destino espera (p. ej. <c>X-Webhook-Signature</c>).</param>
/// <param name="Origen">Quién lo emite: <c>ot</c> (organismo de tránsito) o <c>ict</c>.</param>
/// <param name="Evento">Tipo de evento del webhook (p. ej. <c>procedure.state_changed</c>), para el registro.</param>
public sealed record TrabajoWebhook(string Url, string Cuerpo, IReadOnlyDictionary<string, string> Cabeceras, string Origen, string Evento)
{
    public const string Tipo = "notificaciones.webhook.send";
}
