namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Variante de plantilla del Contrato Privado de Mandato (ADR-0036, HU #10915). Cerrada: cada valor es
/// una redacción legal distinta portada de FLIT 1.0. Añadir una variante exige tocar el generador (mismo
/// criterio que el CHECK <c>ck_transit_office_mandate_config_template</c>).
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
