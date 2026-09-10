namespace Flit.Infrastructure.Notifications.Catalog;

/// <summary>
/// Módulo de dominio dueño de una plantilla de correo (HU #11353). Solo describe DÓNDE vive la
/// plantilla — el catálogo NUNCA importa tipos de esos módulos más allá de lo que ya expone
/// <c>ComposedEmail</c> / <c>(string Subject, string Html)</c>.
/// </summary>
public enum NotificationModule
{
    /// <summary>Flit.Modules.Security.Application — invitación, recuperación y reset administrativo.</summary>
    Security,

    /// <summary>Flit.Infrastructure.Analytics — informe programado y alerta (Reportes 2.0).</summary>
    Analytics,

    /// <summary>Trámites — notificaciones de ciclo de vida (banco de pruebas; disparador productivo diferido).</summary>
    Tramites,
}
