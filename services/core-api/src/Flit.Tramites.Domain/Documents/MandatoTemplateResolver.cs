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

    /// <summary>Mapea el <paramref name="templateCode"/> del OT a su variante; desconocido ⇒ genérico.</summary>
    public static MandatoVariante Resolve(string? templateCode) =>
        (templateCode?.Trim().ToLowerInvariant()) switch
        {
            Sabaneta => MandatoVariante.Sabaneta,
            Bello => MandatoVariante.Bello,
            _ => MandatoVariante.Generico,
        };
}
