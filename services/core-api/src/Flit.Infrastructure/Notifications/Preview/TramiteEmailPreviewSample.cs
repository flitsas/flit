using Flit.Infrastructure.Notifications.Tramites;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestras de <c>tramites.aprobado</c> y <c>tramites.rechazado</c> para el banco de pruebas.
/// Datos sintéticos del mockup de diseño (no PII de personas naturales).
/// </summary>
public static class TramiteEmailPreviewSample
{
    public const string DefaultAssetsBaseUrl = "https://dev.flitsas.online/email-assets";

    /// <summary>Muestra alineada al mockup de trámite aprobado (traspaso + APROBADO).</summary>
    public static TramiteCambioEstadoEmailModel SampleAprobado { get; } = new(
        VendedorNombre: "BANCOLOMBIA S.A",
        CompradorNombre: "EL NEGOCIO DEL ALVARO AMADO",
        Placa: "GSR101",
        CiudadOt: "FUNZA",
        NombreOt: "STRIA TTOyTTE PAL FUNZA",
        EstadoActual: "APROBADO",
        EsTraspaso: true);

    /// <summary>Muestra alineada al mockup de trámite rechazado (traspaso + RECHAZADO).</summary>
    public static TramiteCambioEstadoEmailModel SampleRechazado { get; } = new(
        VendedorNombre: "EL NEGOCIO DE AMADO",
        CompradorNombre: "BANCOLOMBIA S.A",
        Placa: "GSR101",
        CiudadOt: "FUNZA",
        NombreOt: "STRIA TTOyTTE PAL FUNZA",
        EstadoActual: "RECHAZADO",
        EsTraspaso: true,
        CausalesRechazo: ["Documentos ilegibles", "Improntas no coinciden"],
        ObservacionRechazo: "Adjuntar SOAT vigente y fotos nítidas de las improntas.");

    public static (string Subject, string Html) BuildFlitAprobado(string? assetsBaseUrl = null) =>
        TramiteCambioEstadoEmailComposer.ComposeFlit(
            SampleAprobado,
            ResolveBaseUrl(assetsBaseUrl));

    public static (string Subject, string Html) BuildRentingAprobado(string? assetsBaseUrl = null) =>
        TramiteCambioEstadoEmailComposer.ComposeRenting(
            SampleAprobado,
            ResolveBaseUrl(assetsBaseUrl));

    public static (string Subject, string Html) BuildFlitRechazado(string? assetsBaseUrl = null) =>
        TramiteCambioEstadoEmailComposer.ComposeFlit(
            SampleRechazado,
            ResolveBaseUrl(assetsBaseUrl));

    public static (string Subject, string Html) BuildRentingRechazado(string? assetsBaseUrl = null) =>
        TramiteCambioEstadoEmailComposer.ComposeRenting(
            SampleRechazado,
            ResolveBaseUrl(assetsBaseUrl));

    public static TramiteCambioEstadoEmailModel OverlayProcedureType(
        TramiteCambioEstadoEmailModel sample,
        string nombreTipoTramite,
        bool esTraspaso)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample with
        {
            NombreTipoTramite = nombreTipoTramite ?? string.Empty,
            EsTraspaso = esTraspaso,
            VendedorNombre = esTraspaso ? sample.VendedorNombre : string.Empty,
        };
    }

    /// <summary>Compat: apunta a la muestra rechazada (antes «cambio-estado»).</summary>
    public static TramiteCambioEstadoEmailModel SampleModel => SampleRechazado;

    public static (string Subject, string Html) BuildFlit(string? assetsBaseUrl = null) =>
        BuildFlitRechazado(assetsBaseUrl);

    public static (string Subject, string Html) BuildRenting(string? assetsBaseUrl = null) =>
        BuildRentingRechazado(assetsBaseUrl);

    private static string ResolveBaseUrl(string? assetsBaseUrl) =>
        string.IsNullOrWhiteSpace(assetsBaseUrl) ? DefaultAssetsBaseUrl : assetsBaseUrl;
}
