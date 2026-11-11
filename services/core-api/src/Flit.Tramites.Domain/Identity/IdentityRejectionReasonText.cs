using System.Text.Json;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Identity;

/// <summary>
/// Motivo de rechazo de una validación de identidad, listo para REPORTES (pantalla, Excel, CSV).
///
/// <para>Regla de producto: quien lee un reporte no debe saber CÓMO se validó. Por eso el texto sale siempre de un
/// vocabulario cerrado en español: nunca el texto crudo del proveedor ni el de un revisor, y nunca una palabra que
/// delate el mecanismo. Si el origen no encaja en el vocabulario, sale el texto genérico de respaldo.</para>
///
/// <para>No reemplaza a <c>ExtractMotivoRechazo</c> (listado del gestor): es un helper aparte para no cambiar ese
/// comportamiento.</para>
/// </summary>
public static class IdentityRejectionReasonText
{
    /// <summary>Texto de respaldo cuando el motivo de origen no se puede traducir al vocabulario cerrado.</summary>
    public const string Generico = "No se pudo verificar la identidad con las imágenes recibidas.";

    private const string RostroNoCoincide = "El rostro no coincide con el documento.";
    private const string DocumentoNoVerificado = "La verificación del documento no fue exitosa.";
    private const string DatosNoCoinciden = "Los datos personales no coinciden con el documento.";
    private const string ImagenBorrosa = "Imagen borrosa.";
    private const string DocumentoIlegible = "Documento ilegible o incompleto.";

    /// <summary>
    /// Palabras que nunca pueden salir en un reporte. Si el texto de origen las trae, no se reutiliza ni parcialmente.
    /// </summary>
    public static readonly string[] Prohibidas =
        ["kyverum", "manual", "proveedor", "simulad", "migraci", "mock", "webhook", "scorer"];

    /// <summary>
    /// Motivo de la validación, o <c>null</c> si no está rechazada. Orden: código de la lista cerrada de revisión
    /// humana, luego texto de origen traducido por palabras clave, luego respaldo genérico.
    /// </summary>
    public static string? For(ProcedureInstanceBiometricValidation v)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (!string.Equals(v.Status, BiometricEstados.Rechazado, StringComparison.Ordinal))
            return null;

        var porCodigo = ManualRejectionReasons.LabelFor(v.RejectionReasonCode);
        if (porCodigo is not null)
            return Limpio(porCodigo) ? porCodigo + "." : Generico;

        foreach (var origen in new[] { Leer(v.Detail, "motivo"), Leer(v.ProviderPayload, "subject_motivo") })
        {
            var traducido = Traducir(origen);
            if (traducido is not null)
                return traducido;
        }

        var coincidencias = LeerCoincidencias(v.ProviderPayload);
        if (coincidencias.Documento == false)
            return DocumentoNoVerificado;
        if (coincidencias.Nombre == false)
            return DatosNoCoinciden;

        return Generico;
    }

    /// <summary>Traduce un texto libre al vocabulario cerrado, o <c>null</c> si no reconoce ninguna idea.</summary>
    internal static string? Traducir(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var t = texto.Trim().ToLowerInvariant();
        if (t.Contains("borros") || t.Contains("blur"))
            return ImagenBorrosa;
        if (t.Contains("rostro") || t.Contains("facial") || t.Contains("face") || t.Contains("selfie"))
            return RostroNoCoincide;
        if (t.Contains("ilegible") || t.Contains("incomplet"))
            return DocumentoIlegible;
        if (t.Contains("nombre") || t.Contains("datos personales"))
            return DatosNoCoinciden;
        if (t.Contains("documento") || t.Contains("cédula") || t.Contains("cedula"))
            return DocumentoNoVerificado;
        return null;
    }

    /// <summary>¿El texto está libre de palabras que delaten el mecanismo de validación?</summary>
    public static bool Limpio(string texto) =>
        !Prohibidas.Any(p => texto.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string? Leer(string? json, string propiedad)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propiedad, out var p)
                && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (bool? Documento, bool? Nombre) LeerCoincidencias(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("coincidencias", out var c)
                || c.ValueKind != JsonValueKind.Object)
                return (null, null);

            bool? Bool(string n) => c.TryGetProperty(n, out var e)
                ? e.ValueKind switch { JsonValueKind.False => false, JsonValueKind.True => true, _ => null }
                : null;
            return (Bool("documento"), Bool("nombre"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
