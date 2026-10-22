using System.Text.RegularExpressions;

namespace Flit.Infrastructure.Email;

/// <summary>
/// HU #12430 AC6 — saneamiento del nombre visible del remitente antes de aplicarlo a un envío.
/// Defensa en profundidad: MimeKit (<c>MailboxAddress</c>) ya escapa/rechaza CR/LF al construir la
/// cabecera <c>From</c>, pero esta clase es la que decide, de forma explícita y comprobable con
/// tests puros (sin red, sin MailKit), qué queda del texto declarado como
/// <c>tenant_brandings.platform_name</c> antes de mostrarlo a un destinatario final.
/// </summary>
/// <remarks>
/// Reglas (AC6, notas técnicas de la HU): elimina cualquier carácter de control (incluye CR/LF, y
/// por tanto cierra la inyección de cabeceras SMTP), elimina <c>&lt; &gt; @ " , ; :</c> (los
/// caracteres que permitirían simular una segunda dirección o un segundo campo dentro del mismo
/// encabezado), colapsa espacios en blanco consecutivos en uno solo, recorta a 64 caracteres y, si
/// el resultado queda vacío, devuelve <c>null</c> — el llamador aplica entonces su remitente por
/// defecto (<c>EmailSettings.DefaultSenderName</c>).
/// </remarks>
public static class SenderDisplayNameSanitizer
{
    private const int MaxLength = 64;

    // \p{Cc} = caracteres de control (incluye \r \n \0 \t, etc.). Los siete símbolos siguientes son
    // los que permitirían inyectar una dirección, un display-name adicional o un separador de
    // cabecera dentro del propio nombre visible.
    private static readonly Regex ForbiddenCharsPattern = new(
        "[\\p{Cc}<>@\",;:]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceRunPattern = new(
        "\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Sanea <paramref name="input"/> según las reglas de clase. <c>null</c>/vacío/solo-espacios
    /// entra y sale <c>null</c>; un resultado que quede vacío TRAS sanear también devuelve
    /// <c>null</c> (nunca una cadena vacía) para que el llamador pueda usar <c>??</c> con su valor
    /// por defecto sin comprobaciones adicionales.
    /// </summary>
    public static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var withoutForbidden = ForbiddenCharsPattern.Replace(input, string.Empty);
        var collapsed = WhitespaceRunPattern.Replace(withoutForbidden, " ").Trim();

        if (collapsed.Length == 0)
            return null;

        if (collapsed.Length <= MaxLength)
            return collapsed;

        // Recorte a 64 caracteres (por unidad UTF-16, igual que string.Length) sin dejar un espacio
        // colgando en el borde del corte.
        return TrimToMaxLength(collapsed);
    }

    private static string TrimToMaxLength(string value)
    {
        var truncated = value[..MaxLength].TrimEnd();
        return truncated.Length == 0 ? value[..MaxLength] : truncated;
    }
}
