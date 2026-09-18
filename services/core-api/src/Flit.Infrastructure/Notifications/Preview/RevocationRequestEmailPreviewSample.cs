using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.RevocationRequests;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestras de <c>tramites.revocatoria-solicitada</c> / <c>-aprobada</c> / <c>-rechazada</c> para el
/// banco de pruebas (HU #12579, Feature #12565). Una por hito — mismo criterio que
/// <see cref="AsignacionPlacaEmailPreviewSample"/> para su única plantilla.
/// </summary>
public static class RevocationRequestEmailPreviewSample
{
    public const string DefaultAssetsBaseUrl = "https://dev.flitsas.online/email-assets";

    public static RevocationRequestEmailModel SampleSolicitada { get; } = new(
        DestinatarioNombre: "Juan Carlos Pérez Gómez",
        Placa: "ABC123",
        Radicado: "FT1-0000012");

    public static RevocationRequestEmailModel SampleAprobada { get; } = new(
        DestinatarioNombre: "Juan Carlos Pérez Gómez",
        Placa: "ABC123",
        Radicado: "FT1-0000012");

    public static RevocationRequestEmailModel SampleRechazada { get; } = new(
        DestinatarioNombre: "Juan Carlos Pérez Gómez",
        Placa: "ABC123",
        Radicado: "FT1-0000012",
        Motivo: "El documento de soporte adjunto no es legible.");

    /// <param name="theme">HU #12428 — tema resuelto por red; <c>null</c>/<c>Flit</c> deja la
    /// muestra idéntica a la variante FLIT (AC9).</param>
    public static (string Subject, string Html) BuildFlit(
        string milestone, string? assetsBaseUrl = null, EmailTheme? theme = null) =>
        RevocationRequestEmailComposer.ComposeFlit(
            milestone,
            SampleFor(milestone),
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? DefaultAssetsBaseUrl : assetsBaseUrl,
            theme);

    public static (string Subject, string Html) BuildRenting(string milestone, string? assetsBaseUrl = null) =>
        RevocationRequestEmailComposer.ComposeRenting(
            milestone,
            SampleFor(milestone),
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? DefaultAssetsBaseUrl : assetsBaseUrl);

    private static RevocationRequestEmailModel SampleFor(string milestone) => milestone switch
    {
        RevocationRequestEmailMilestone.Solicitada => SampleSolicitada,
        RevocationRequestEmailMilestone.Aprobada => SampleAprobada,
        RevocationRequestEmailMilestone.Rechazada => SampleRechazada,
        _ => throw new ArgumentOutOfRangeException(nameof(milestone), milestone, "Hito de revocatoria desconocido."),
    };
}
