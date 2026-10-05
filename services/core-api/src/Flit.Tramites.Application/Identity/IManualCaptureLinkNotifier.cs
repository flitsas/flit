namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Enlace de captura manual recién generado (HU #13284, Épica #13202). <see cref="Token"/> es el token EN CLARO: existe solo
/// en memoria, entre el caso de uso que lo genera y el notificador; en la base se guarda únicamente su hash SHA-256
/// (<c>TokenHash</c>) y nunca viaja en la respuesta HTTP ni en logs ni en la auditoría.
/// </summary>
public sealed record ManualCaptureLink(
    Guid ValidationId,
    Guid TenantId,
    string Token,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Puerto de salida del enlace de captura manual. El caso de uso de activación lo invoca UNA vez, con el token en claro,
/// DESPUÉS de persistir la activación. HU-A5 (#13287) lo implementa con el envío del correo al cliente (reutilizando el canal
/// del reenvío de identidad) y reemplaza el registro por defecto.
/// <para>
/// Decisión de diseño: el token en claro NO se devuelve al navegador del Super Admin; el backend lo entrega por correo al
/// titular. Hasta que A5 exista, el registro por defecto es <see cref="NoOpManualCaptureLinkNotifier"/> (no envía nada).
/// Las implementaciones NO deben lanzar: la activación ya está confirmada y, si el envío falla, el Super Admin regenera el
/// enlace (A5).
/// </para>
/// </summary>
public interface IManualCaptureLinkNotifier
{
    Task NotifyAsync(ManualCaptureLink link, CancellationToken ct = default);
}

/// <summary>
/// Implementación por defecto, sin efecto: el enlace no se envía ni se registra (el token en claro se descarta). Existe para
/// que la activación (HU #13284) funcione y se pruebe antes de HU-A5; mientras sea esta la implementación activa, un enlace
/// activado no llega al cliente. Sustituir al implementar HU #13287.
/// </summary>
public sealed class NoOpManualCaptureLinkNotifier : IManualCaptureLinkNotifier
{
    public Task NotifyAsync(ManualCaptureLink link, CancellationToken ct = default) => Task.CompletedTask;
}
