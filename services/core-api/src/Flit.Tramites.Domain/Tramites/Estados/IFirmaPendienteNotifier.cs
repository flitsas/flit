namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Bug #13194 (P4, D2) — puerto que el gate de firma usa para disparar, por cada parte sin firma, el
/// correo de validación de identidad (asegurar identidad + iniciar la validación si hace falta). Es
/// idempotente: una validación en curso no se duplica. Lo implementa la capa de aplicación sobre
/// <c>EnsureIdentityAndNotifyHandler</c>; el ciclo de vida solo conoce el puerto (sin ciclo de DI).
/// </summary>
public interface IFirmaPendienteNotifier
{
    /// <summary>
    /// Asegura la identidad de <paramref name="parte"/> y notifica si hace falta. Devuelve el estado de la
    /// notificación (<see cref="FirmaNotificacionEstados"/>). Puede lanzar: el llamador lo trata como
    /// <see cref="FirmaNotificacionEstados.Fallida"/>.
    /// </summary>
    Task<string> NotificarAsync(Guid instanceId, Guid tenantId, string parte, CancellationToken ct = default);
}

/// <summary>Estados de la notificación de una parte sin firma (mismos códigos que el handler de Y).</summary>
public static class FirmaNotificacionEstados
{
    /// <summary>No hacía falta: la identidad ya quedó cubierta.</summary>
    public const string NoRequerida = "no_requerida";

    /// <summary>Se inició la validación y salió el correo.</summary>
    public const string Enviada = "enviada";

    /// <summary>Ya había una validación en curso: no se duplicó la sesión.</summary>
    public const string YaEnCurso = "ya_en_curso";

    /// <summary>Hacía falta y no se pudo iniciar (proveedor, datos, excepción).</summary>
    public const string Fallida = "fallida";

    /// <summary>No hay notificador cableado (tests, composiciones mínimas).</summary>
    public const string NoConfigurada = "no_configurada";
}

/// <summary>Parte que debe firmar y no tiene identidad aprobada y vigente, con el estado de su notificación.</summary>
public sealed record ParteSinFirma(string Parte, string Notificacion);
