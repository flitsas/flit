namespace Flit.Tramites.Application.Identity;

/// <summary>
/// HU #14301 — textos legales que la persona ACEPTÓ en el flujo manual (captura por enlace), por versión de texto
/// (<c>consent_text_version</c> de la validación). Los textos viven hoy en el front (<c>frontend/lib/captura-manual/consent.ts</c>:
/// <c>CONSENT_TEXT_BODY</c>, <c>AUTORIZACION_FIRMA_TITLE</c>, <c>AUTORIZACION_FIRMA_TEXT</c>); esta tabla es su copia literal del
/// lado del servidor para imprimirlos en la carta. <b>No hay fuente única compartida</b>: una prueba
/// (<c>ManualIdentityAuthorizationTextsParityTests</c>) falla si divergen. Una versión desconocida NO se reemplaza por otro texto:
/// la carta imprime solo la versión (no se inventa un texto que la persona no aceptó). Cualquier cambio de redacción exige subir la
/// versión (<c>ManualCaptureConsent.TextVersion</c>) y agregar una entrada nueva, sin tocar las anteriores.
/// </summary>
public static class ManualIdentityAuthorizationTexts
{
    /// <summary>Textos de una versión: consentimiento biométrico (paso Datos) y autorización de la firma (paso Firma).</summary>
    public sealed record Texts(string ConsentBody, string PrivacyLabel, string AuthorizationTitle, string AuthorizationText);

    private static readonly Dictionary<string, Texts> ByVersion = new(StringComparer.Ordinal)
    {
        ["manual-ley1581-v2"] = new Texts(
            ConsentBody:
            "Autorizo de forma libre, previa y expresa a Flit, que es quien guarda mis datos, el tratamiento de mis datos biométricos (dato sensible) para verificar mi identidad (Ley 1581 de 2012). No estoy obligado(a).",
            PrivacyLabel: "Aviso de privacidad",
            AuthorizationTitle: "Autorización de trámite digital",
            AuthorizationText:
            "Declaro que adelanto esta verificación de forma libre y voluntaria. Autorizo a Flit el tratamiento de mis datos personales, incluidos mis datos biométricos y mi firma, que son datos sensibles, únicamente para verificar mi identidad. Flit es quien guarda esos datos."),
    };

    /// <summary>Textos de la versión indicada, o <c>null</c> si no se conocen (no se sustituyen por otra versión).</summary>
    public static Texts? For(string? version) =>
        version is not null && ByVersion.TryGetValue(version.Trim(), out var texts) ? texts : null;

    /// <summary>Versiones con texto conocido (las usa la prueba de paridad con el front).</summary>
    public static IReadOnlyCollection<string> KnownVersions => ByVersion.Keys;
}
