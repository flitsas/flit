namespace Flit.Tramites.Application.Notifications;

/// <summary>HU #11462 — tipo de cupo de notificación al cambio de estado.</summary>
public enum TramiteRecipientKind
{
    Persona,
    Empresa,
    RepresentanteLegal,
}

/// <summary>Destinatario resuelto con correo.</summary>
public sealed record TramiteEmailRecipient(
    string Role,
    TramiteRecipientKind Kind,
    string Email,
    string DisplayName);

/// <summary>Cupo declarado sin correo resoluble (queda <c>omitido</c> en la cola).</summary>
public sealed record TramiteRecipientGap(
    string Role,
    TramiteRecipientKind Kind,
    string? DisplayName);

/// <summary>Resultado de la resolución: destinatarios + huecos, en orden determinista.</summary>
public sealed record TramiteRecipientResolution(
    IReadOnlyList<TramiteEmailRecipient> Recipients,
    IReadOnlyList<TramiteRecipientGap> Gaps);
