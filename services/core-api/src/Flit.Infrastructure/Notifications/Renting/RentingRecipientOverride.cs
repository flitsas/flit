namespace Flit.Infrastructure.Notifications.Renting;

/// <summary>
/// Punto de encaje para la HU #11364 (desvío de destinatario fuera de producción — FUERA DE
/// ALCANCE de la HU #11361, que solo modela el hueco donde esa HU se conecta). Se invoca DESPUÉS
/// de construir el <see cref="RentingSendEmailRequest"/> definitivo y ANTES de construir el
/// multipart (ver <see cref="RentingEmailApiSender.SendAsync"/>), para que la sustitución de
/// destinatarios (reales → <c>RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_*</c>, ver
/// <see cref="RentingChannelOptions"/>) no tenga que reimplementar nada del armado del cuerpo ni
/// del envío/autenticación.
/// </summary>
public interface IRentingRecipientOverride
{
    /// <summary>
    /// Devuelve la petición a enviar. La implementación por defecto (<see cref="PassthroughRentingRecipientOverride"/>)
    /// no modifica nada — la HU #11364 registra la implementación real (gate por ambiente) sin que
    /// esta HU tenga que anticiparla.
    /// </summary>
    RentingSendEmailRequest Apply(RentingSendEmailRequest request);
}

/// <summary>
/// Implementación por defecto de <see cref="IRentingRecipientOverride"/>: no desvía nada. Es la
/// registrada por esta HU (#11361); la HU #11364 la reemplaza cuando implemente el desvío real.
/// </summary>
internal sealed class PassthroughRentingRecipientOverride : IRentingRecipientOverride
{
    public RentingSendEmailRequest Apply(RentingSendEmailRequest request) => request;
}
