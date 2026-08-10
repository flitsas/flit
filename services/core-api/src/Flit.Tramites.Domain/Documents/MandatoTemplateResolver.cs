namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Familia del MANDATARIO del contrato (HU #11204, metadato <c>familia_mandatario</c> de las plantillas
/// del PO): quién firma como mandatario.
///
/// <para><b>La familia NO determina la redacción.</b> Bello y Sabaneta son ambos
/// <see cref="OrganismoTransito"/> y su texto legal difiere —Bello nombra al representante legal de la
/// unión temporal, Sabaneta nombra a la unión temporal directamente—. La familia describe QUIÉN es el
/// mandatario; la redacción la sigue eligiendo el <c>template_code</c>.</para>
/// </summary>
public enum MandatoFamilia
{
    /// <summary>El mandatario es una persona natural (el firmante registrado del OT).</summary>
    Individuo,

    /// <summary>El mandatario es el propio organismo / una unión temporal (persona jurídica).</summary>
    OrganismoTransito,
}

/// <summary>Códigos de familia tal como viajan en la configuración del OT.</summary>
public static class MandatoFamiliaCodes
{
    public const string Individuo = "individuo";
    public const string OrganismoTransito = "organismo_transito";

    /// <summary>Mapea el código de familia; desconocido o ausente ⇒ <see cref="MandatoFamilia.Individuo"/>.</summary>
    public static MandatoFamilia Resolve(string? familia) =>
        string.Equals(familia?.Trim(), OrganismoTransito, StringComparison.OrdinalIgnoreCase)
            ? MandatoFamilia.OrganismoTransito
            : MandatoFamilia.Individuo;
}

/// <summary>
/// Modo de asignación del mandatario (capa de negocio Plataforma). Independiente de
/// <see cref="MandatoVariante"/> (redacción legal) y de <see cref="MandatoFamilia"/>.
/// </summary>
public static class MandatoAssignmentModeCodes
{
    /// <summary>Persona o RL firma el bloque del mandatario (default).</summary>
    public const string Signer = "signer";

    /// <summary>El OT / unión temporal actúa como mandatario (sin firmante persona).</summary>
    public const string Institutional = "institutional";

    /// <summary>Contrato abierto: sin mandatario asignado (placeholders en el PDF).</summary>
    public const string Open = "open";

    /// <summary>Desconocido o ausente ⇒ <see cref="Signer"/> (comportamiento histórico).</summary>
    public static string Resolve(string? mode)
    {
        var value = mode?.Trim().ToLowerInvariant();
        return value switch
        {
            Institutional => Institutional,
            Open => Open,
            _ => Signer,
        };
    }

    /// <summary>True si el flujo no debe exigir ni fijar un firmante persona (institucional u abierto).</summary>
    public static bool SkipsPersonSigner(string? mode)
    {
        var resolved = Resolve(mode);
        return resolved is Institutional or Open;
    }

    /// <summary>True si el OT/UT actúa como mandatario (sin bloque de firma persona).</summary>
    public static bool IsInstitutional(string? mode) =>
        Resolve(mode) == Institutional;

    /// <summary>
    /// True si el contrato queda sin mandatario asignado: cuerpo y bloque de firma con placeholders
    /// (<c>___</c> / líneas), no se oculta el bloque.
    /// </summary>
    public static bool IsOpen(string? mode) =>
        Resolve(mode) == Open;
}

/// <summary>Origen de la plantilla del mandato por OT.</summary>
public static class MandatoCustomTemplateKindCodes
{
    public const string None = "none";
    public const string Pdf = "pdf";
    public const string Editor = "editor";

    public static string Resolve(string? kind)
    {
        var value = kind?.Trim().ToLowerInvariant();
        return value switch
        {
            Pdf => Pdf,
            Editor => Editor,
            _ => None,
        };
    }

    public static bool HasCustom(string? kind)
    {
        var resolved = Resolve(kind);
        return resolved is Pdf or Editor;
    }
}

/// <summary>
/// Variante de plantilla del Contrato Privado de Mandato (ADR-0036, HU #10915). Cada valor es una
/// REDACCIÓN legal distinta portada de FLIT 1.0. Añadir una redacción nueva exige tocar el generador;
/// reutilizar una existente en otro organismo, no (HU #11204: los datos propios del OT —ciudad de la
/// Cámara, sigla, razón social y NIT— viven en la configuración, y el CHECK cerrado se retiró).
/// </summary>
public enum MandatoVariante
{
    /// <summary>Plantilla genérica: el MANDATARIO es una persona (el firmante registrado del OT). Ambas partes firman.</summary>
    Generico,

    /// <summary>Sabaneta: MANDATARIO institucional fijo (UT-SETSA); solo firma el MANDANTE.</summary>
    Sabaneta,

    /// <summary>Bello: el MANDATARIO es una persona, representante legal de la UT-MAB. Ambas partes firman.</summary>
    Bello,

    /// <summary>
    /// Redacción corta PN/PJ (Envigado, Funza, Medellín y equivalentes): intro + objeto en un párrafo,
    /// OBLIGACIONES y cierre por ciudad del OT. Firmante persona; ambas partes firman.
    /// </summary>
    Municipio,
}

/// <summary>
/// Resuelve la variante de plantilla del mandato a partir del <c>template_code</c> configurado para el OT
/// (ADR-0036, HU #10912/#10915). Función <b>pura</b> y cerrada (mismos valores que el CHECK de la config):
/// un código desconocido o ausente cae a la plantilla <see cref="MandatoVariante.Generico"/>. La distinción
/// persona natural / jurídica NO la decide esta función, sino el generador con los datos del mandante.
/// </summary>
public static class MandatoTemplateResolver
{
    /// <summary>Código de la plantilla genérica (default cuando el OT no tiene configuración de mandato).</summary>
    public const string Generico = "generico";

    /// <summary>Código de la plantilla de Sabaneta (UT-SETSA).</summary>
    public const string Sabaneta = "sabaneta";

    /// <summary>Código de la plantilla de Bello (UT-MAB).</summary>
    public const string Bello = "bello";

    /// <summary>Código de la redacción corta municipal (Envigado / Funza / Medellín).</summary>
    public const string Municipio = "municipio";

    /// <summary>Mapea el <paramref name="templateCode"/> del OT a su variante; desconocido ⇒ genérico.</summary>
    public static MandatoVariante Resolve(string? templateCode) =>
        (templateCode?.Trim().ToLowerInvariant()) switch
        {
            Sabaneta => MandatoVariante.Sabaneta,
            Bello => MandatoVariante.Bello,
            Municipio => MandatoVariante.Municipio,
            _ => MandatoVariante.Generico,
        };
}

/// <summary>
/// Plantillas de sistema ligadas a códigos RUNT de OT (Sabaneta / Bello / municipales PN corta).
/// Prioridad de redacción en trámite:
/// <list type="number">
///   <item>Plantilla propia cargada (PDF/editor).</item>
///   <item>Builtin por código de OT (Sabaneta, Bello, Envigado, Funza, Medellín).</item>
///   <item>Config explícita de otro OT (<c>template_code</c>).</item>
///   <item>Genérica.</item>
/// </list>
/// </summary>
public static class MandatoSystemOfficeTemplates
{
    public const string SabanetaOfficeCode = "5631000";
    public const string BelloOfficeCode = "5088000";
    public const string EnvigadoOfficeCode = "5266000";
    public const string FunzaOfficeCode = "25286000";
    public const string MedellinOfficeCode = "5001000";

    public sealed record Builtin(
        string TemplateCode,
        string MandataryFamily,
        bool RequiresForNaturalPerson,
        string InstitutionalMandataryName,
        string InstitutionalMandataryNit,
        string? ChamberCity,
        string? MandatarySigla);

    public static readonly Builtin Sabaneta = new(
        MandatoTemplateResolver.Sabaneta,
        MandatoFamiliaCodes.OrganismoTransito,
        RequiresForNaturalPerson: true,
        "UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA",
        "900273813-7",
        "Medellín",
        "UT-SETSA");

    public static readonly Builtin Bello = new(
        MandatoTemplateResolver.Bello,
        MandatoFamiliaCodes.OrganismoTransito,
        RequiresForNaturalPerson: true,
        "UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB",
        "901783814-6",
        "Medellín",
        null);

    /// <summary>Redacción corta compartida; la ciudad del cierre sale del OT del trámite.</summary>
    public static readonly Builtin Municipio = new(
        MandatoTemplateResolver.Municipio,
        MandatoFamiliaCodes.Individuo,
        RequiresForNaturalPerson: true,
        string.Empty,
        string.Empty,
        null,
        null);

    public static Builtin? TryGetByOfficeCode(string? officeCode)
    {
        var code = officeCode?.Trim();
        if (string.IsNullOrEmpty(code))
            return null;
        if (string.Equals(code, SabanetaOfficeCode, StringComparison.OrdinalIgnoreCase))
            return Sabaneta;
        if (string.Equals(code, BelloOfficeCode, StringComparison.OrdinalIgnoreCase))
            return Bello;
        if (string.Equals(code, EnvigadoOfficeCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, FunzaOfficeCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(code, MedellinOfficeCode, StringComparison.OrdinalIgnoreCase))
            return Municipio;
        return null;
    }

    /// <summary>
    /// Código de plantilla efectivo para generar el PDF (sin bytes custom).
    /// Con plantilla propia el caller usa custom; este método solo resuelve la redacción base.
    /// </summary>
    public static string ResolveTemplateCode(
        string officeCode,
        string? configuredTemplateCode,
        string? customTemplateKind)
    {
        // Propia: se conserva el template_code de la fila como base (editor/overlay); el generador
        // prioriza el artefacto custom. Si no hay código, cae a genérico.
        if (MandatoCustomTemplateKindCodes.HasCustom(customTemplateKind))
        {
            return string.IsNullOrWhiteSpace(configuredTemplateCode)
                ? MandatoTemplateResolver.Generico
                : configuredTemplateCode.Trim().ToLowerInvariant();
        }

        var builtin = TryGetByOfficeCode(officeCode);
        if (builtin is not null)
            return builtin.TemplateCode;

        if (!string.IsNullOrWhiteSpace(configuredTemplateCode))
            return configuredTemplateCode.Trim().ToLowerInvariant();

        return MandatoTemplateResolver.Generico;
    }
}
