using Flit.Infrastructure.Notifications.Tramites;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestra de <c>tramites.asignacion-placa</c> para el banco de pruebas.
/// </summary>
public static class AsignacionPlacaEmailPreviewSample
{
    public const string DefaultAssetsBaseUrl = "https://dev.flitsas.online/email-assets";

    public static AsignacionPlacaEmailModel Sample { get; } = new(
        ClienteNombre: "Juan Carlos Pérez Gómez",
        Placa: "ABC123",
        Ciudad: "Medellín",
        SecretariaTransito: "Secretaría de Movilidad de Medellín",
        EstadoActual: "Asignado");

    public static (string Subject, string Html) BuildFlit(string? assetsBaseUrl = null) =>
        AsignacionPlacaEmailComposer.ComposeFlit(
            Sample,
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? DefaultAssetsBaseUrl : assetsBaseUrl);

    public static (string Subject, string Html) BuildRenting(string? assetsBaseUrl = null) =>
        AsignacionPlacaEmailComposer.ComposeRenting(
            Sample,
            string.IsNullOrWhiteSpace(assetsBaseUrl) ? DefaultAssetsBaseUrl : assetsBaseUrl);
}
