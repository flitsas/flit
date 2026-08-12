using System.Net;
using System.Text;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// Datos de la plantilla <c>tramites.asignacion-placa</c> (banco de pruebas).
/// </summary>
/// <remarks>
/// En productivo la variante Renting aplica cuando
/// <c>companyRegistered == 811011779</c>; la FLIT genérica en el resto. En el banco de pruebas
/// la variante se elige por canal (<c>FLIT_SMTP</c> / <c>TENANT_API</c>).
/// </remarks>
public sealed record AsignacionPlacaEmailModel(
    string ClienteNombre,
    string Placa,
    string Ciudad,
    string SecretariaTransito,
    string EstadoActual);

/// <summary>
/// Composer de <c>tramites.asignacion-placa</c>: variante FLIT genérica y Renting Colombia.
/// </summary>
public static class AsignacionPlacaEmailComposer
{
    public const string TemplateId = "tramites.asignacion-placa";

    /// <summary>NIT de Renting Colombia que selecciona la variante Renting en productivo.</summary>
    public const string RentingCompanyRegisteredNit = "811011779";

    private const string Ink = "#1A1A1A";
    private const string Muted = "#4A4A4A";
    private const string FlitSupportEmail = "soporte@flitsas.com";
    private const string RentingSupportEmail = "servicio@rentingcolombia.com";
    private const string RentingWebFormsUrl =
        "https://www.rentingcolombia.com/dejanos-tus-comentarios-te-asesoramos";

    public const string DefaultLogoFileName = "flit-logo.png";
    public const string DefaultRentingLogoFileName = "renting-colombia-logo.png";

    public static string ResolveFlitLogoUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultLogoFileName);

    public static string ResolveRentingLogoUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultRentingLogoFileName);

    public static (string Subject, string Html) ComposeFlit(
        AsignacionPlacaEmailModel model, string assetsBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(model);
        var estado = NormalizeEstado(model.EstadoActual);
        var subject = BuildSubject(model.Placa, estado);
        return (subject, BuildFlitHtml(model, estado, assetsBaseUrl));
    }

    public static (string Subject, string Html) ComposeRenting(
        AsignacionPlacaEmailModel model, string assetsBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(model);
        var estado = NormalizeEstado(model.EstadoActual);
        var subject = BuildSubject(model.Placa, estado);
        return (subject, BuildRentingHtml(model, estado, assetsBaseUrl));
    }

    private static string BuildSubject(string placa, string estado) =>
        $"[FLIT] Asignación de placa — {placa} — {estado}";

    private static string NormalizeEstado(string estado) =>
        string.IsNullOrWhiteSpace(estado) ? "Asignado" : estado.Trim();

    private static string BuildFlitHtml(
        AsignacionPlacaEmailModel model, string estado, string assetsBaseUrl)
    {
        var body = BuildSharedBody(model, estado);
        var logoUrl = EncAttr(ResolveFlitLogoUrl(assetsBaseUrl));
        var support = Enc(FlitSupportEmail);
        var supportMailto = EncAttr($"mailto:{FlitSupportEmail}");

        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light dark\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light dark\"/>");
        sb.Append("<title>Asignación de placa</title>");
        sb.Append("<style type=\"text/css\">");
        sb.Append(".placa-bg{background-color:#ffffff !important;}");
        sb.Append("@media (prefers-color-scheme:dark){.placa-bg{background-color:#ffffff !important;}}");
        sb.Append("</style></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" class=\"placa-bg\" bgcolor=\"#ffffff\" style=\"background-color:#ffffff !important;\">");
        sb.Append("<tr><td align=\"center\" style=\"padding:24px 16px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;font-family:Arial,Helvetica,sans-serif;color:")
            .Append(Ink).Append(";font-size:14px;line-height:1.5;\">");
        sb.Append(body);
        sb.Append("<tr><td style=\"padding:8px 0 20px;\">Atentamente,</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 24px;\">");
        sb.Append("<img src=\"").Append(logoUrl)
            .Append("\" alt=\"flit\" width=\"96\" style=\"display:block;border:0;height:auto;\"/>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0;font-size:12px;line-height:1.45;color:")
            .Append(Muted).Append(";\">");
        sb.Append("Por favor, <strong>NO respondas a este mensaje, esto es un mensaje o una notificación automática.</strong> ");
        sb.Append("Si requieres ayuda o mas información sobre éste, puedes escribirnos a nuestro correo de soporte nacional ");
        sb.Append("<a href=\"").Append(supportMailto).Append("\" style=\"color:")
            .Append(Ink).Append(";font-weight:bold;text-decoration:underline;\">")
            .Append(support).Append("</a>");
        sb.Append("</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string BuildRentingHtml(
        AsignacionPlacaEmailModel model, string estado, string assetsBaseUrl)
    {
        var body = BuildSharedBody(model, estado);
        var rentingLogoUrl = EncAttr(ResolveRentingLogoUrl(assetsBaseUrl));
        var flitLogoUrl = EncAttr(ResolveFlitLogoUrl(assetsBaseUrl));
        var support = Enc(RentingSupportEmail);
        var supportMailto = EncAttr($"mailto:{RentingSupportEmail}");
        var formsUrl = EncAttr(RentingWebFormsUrl);
        var formsDisplay = Enc(RentingWebFormsUrl);

        var sb = new StringBuilder(3072);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light dark\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light dark\"/>");
        sb.Append("<title>Asignación de placa</title>");
        sb.Append("<style type=\"text/css\">");
        sb.Append(".placa-bg{background-color:#ffffff !important;}");
        sb.Append("@media (prefers-color-scheme:dark){.placa-bg{background-color:#ffffff !important;}}");
        sb.Append("</style></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" class=\"placa-bg\" bgcolor=\"#ffffff\" style=\"background-color:#ffffff !important;\">");
        sb.Append("<tr><td align=\"center\" style=\"padding:24px 16px;\">");
        sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;font-family:Arial,Helvetica,sans-serif;color:")
            .Append(Ink).Append(";font-size:14px;line-height:1.5;\">");
        sb.Append("<tr><td align=\"center\" style=\"padding:0 0 28px;\">");
        sb.Append("<img src=\"").Append(rentingLogoUrl)
            .Append("\" alt=\"Renting Colombia\" width=\"167\" style=\"display:block;border:0;height:auto;margin:0 auto;\"/>");
        sb.Append("</td></tr>");
        sb.Append(body);
        sb.Append("<tr><td style=\"padding:4px 0 6px;\"><strong>Línea gratuita</strong> 018000524444 o desde Medellín (604) 5144444 opción 4</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 6px;\"><strong>WhatsApp</strong> +57 3508285539</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 6px;\"><strong>Formularios web</strong> <a href=\"")
            .Append(formsUrl).Append("\" style=\"color:")
            .Append(Ink).Append(";text-decoration:underline;\">")
            .Append(formsDisplay).Append("</a></td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 20px;\"><strong>Correo electrónico:</strong> <a href=\"")
            .Append(supportMailto).Append("\" style=\"color:")
            .Append(Ink).Append(";text-decoration:underline;\">")
            .Append(support).Append("</a></td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 20px;\">Atentamente,</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 24px;\">");
        sb.Append("<img src=\"").Append(flitLogoUrl)
            .Append("\" alt=\"flit\" width=\"96\" style=\"display:block;border:0;height:auto;\"/>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0;font-size:12px;line-height:1.45;color:")
            .Append(Muted).Append(";\">");
        sb.Append("Por favor, <strong>NO respondas a este mensaje, esto es un mensaje o una notificación automática.</strong> ");
        sb.Append("Si requieres ayuda o mas información sobre éste, puedes escribirnos a nuestro correo de soporte nacional ");
        sb.Append("<a href=\"").Append(supportMailto).Append("\" style=\"color:")
            .Append(Ink).Append(";font-weight:bold;text-decoration:underline;\">")
            .Append(support).Append("</a>");
        sb.Append("</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string BuildSharedBody(AsignacionPlacaEmailModel model, string estado)
    {
        var cliente = Enc(model.ClienteNombre);
        var placa = Enc(model.Placa);
        var ciudad = Enc(model.Ciudad);
        var secretaria = Enc(model.SecretariaTransito);
        var estadoEnc = Enc(estado);

        var sb = new StringBuilder(1024);
        sb.Append("<tr><td style=\"padding:0 0 16px;\">Estimados Señor/a <strong>")
            .Append(cliente).Append("</strong>.</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 20px;\">Queremos mantenerlos informados sobre el estado actualizado de su matricula inicial vehículo cuya posible placa es: <strong>")
            .Append(placa).Append("</strong>.</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 8px;\"><strong>Detalles clave:</strong></td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 4px;\"><strong>Cliente:</strong> ")
            .Append(cliente).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 4px;\"><strong>Placa del Vehículo:</strong> ")
            .Append(placa).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 4px;\"><strong>Ciudad:</strong> ")
            .Append(ciudad).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 16px;\"><strong>Secretaría de Tránsito:</strong> ")
            .Append(secretaria).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 20px;\">Estado Actual: <strong>")
            .Append(estadoEnc).Append("</strong></td></tr>");
        sb.Append("<tr><td style=\"padding:0 0 20px;\">Nuestro compromiso es asegurar un proceso sin contratiempos. Les proporcionaremos cualquier novedad y estamos disponibles para responder sus preguntas a través de nuestros canales de contacto.</td></tr>");
        return sb.ToString();
    }

    private static string CombineAssetUrl(string assetsBaseUrl, string fileName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? "https://dev.flitsas.online/email-assets"
            : assetsBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/{fileName}";
    }

    private static string Enc(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string EncAttr(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
