namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Enlace de captura manual recién generado (HU #13284/#13287, Épica #13202). <see cref="Token"/> es el token EN CLARO: existe
/// solo en memoria, entre el caso de uso que lo genera y el notificador; en la base se guarda únicamente su hash SHA-256
/// (<c>TokenHash</c>) y nunca viaja en la respuesta HTTP ni en logs ni en la auditoría. <see cref="RecipientEmail"/> y
/// <see cref="RecipientName"/> son los del titular (PII): solo para componer el correo, jamás para registrarlos.
/// <para>
/// <see cref="RejectionReasonLabel"/> (HU #13299): solo cuando el enlace nuevo sale de un RECHAZO de la revisión manual; es la
/// etiqueta en español del motivo (lista cerrada, <c>ManualRejectionReasons</c>), nunca texto libre. El correo la muestra junto al
/// enlace; en una activación o regeneración es <c>null</c> y el correo es el de siempre.
/// </para>
/// </summary>
public sealed record ManualCaptureLink(
    Guid ValidationId,
    Guid TenantId,
    string Token,
    DateTimeOffset ExpiresAt,
    string? RecipientEmail,
    string? RecipientName,
    string? RejectionReasonLabel = null);

/// <summary>
/// Puerto de salida del enlace de captura manual. El caso de uso de activación (y el de regeneración) lo invoca UNA vez, con el
/// token en claro, DESPUÉS de persistir. HU #13287 lo implementa enviando el correo al titular por el mismo canal de correo de
/// la plataforma (<c>IEmailSender</c>).
/// <para>
/// Devuelve <c>true</c> si el correo fue aceptado para entrega y <c>false</c> si no salió (sin correo del titular, fallo del
/// proveedor): la activación ya está confirmada, así que el fallo NO se revierte; el caso de uso lo audita y lo informa al Super
/// Admin, que puede regenerar. Las implementaciones NO deben lanzar.
/// </para>
/// </summary>
public interface IManualCaptureLinkNotifier
{
    Task<bool> NotifyAsync(ManualCaptureLink link, CancellationToken ct = default);
}
