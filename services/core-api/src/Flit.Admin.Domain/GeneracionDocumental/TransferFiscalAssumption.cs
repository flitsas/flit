namespace Flit.Admin.Domain.GeneracionDocumental;

/// <summary>
/// Catálogos de las tres variables fiscales del anexo §5.4 —<c>{{asume_retencion_fuente}}</c>,
/// <c>{{asume_derechos_tramite}}</c> y <c>{{asume_impuesto_vehiculo}}</c>— que imprime la cláusula
/// SEXTA del documento.
///
/// <para><b>Por qué viven en el dominio y no dentro del handler.</b> Nacieron como dos arrays
/// privados de <c>GenerateTransferenciaHandler</c>. Mientras el único que capturaba estos valores
/// era el formulario, eso bastaba. Con la plantilla XLSX hay un SEGUNDO sitio que debe ofrecer
/// exactamente el mismo catálogo —el desplegable de la columna— y un catálogo copiado a mano es un
/// desplegable que ofrece un valor que el servidor descarta en silencio: <c>NormalizeCatalog</c>
/// devuelve <c>null</c> ante un valor desconocido, así que la cláusula saldría sin la asunción que
/// el usuario eligió y nadie vería un error.</para>
///
/// <para><b>«Según la ley» no existe en los derechos de trámite</b> (§5.4). No es una omisión:
/// los derechos de trámite se pagan, y la pregunta es quién los paga o si se reparten. Por eso son
/// dos catálogos y no uno con un valor de más.</para>
/// </summary>
public static class TransferFiscalAssumption
{
    public const string Transferente = "TRANSFERENTE";
    public const string Adquirente = "ADQUIRENTE";

    /// <summary>La asunción la determina la ley, no el acuerdo de las partes.</summary>
    public const string SegunLey = "SEGUN_LEY";

    /// <summary>Ambas partes, en proporciones iguales. Solo para derechos de trámite.</summary>
    public const string Compartidos = "COMPARTIDOS";

    /// <summary>Catálogo de <c>{{asume_retencion_fuente}}</c> y <c>{{asume_impuesto_vehiculo}}</c>.</summary>
    public static IReadOnlyList<string> ConLey { get; } = [Transferente, Adquirente, SegunLey];

    /// <summary>Catálogo de <c>{{asume_derechos_tramite}}</c>: aquí no existe «según la ley».</summary>
    public static IReadOnlyList<string> Derechos { get; } = [Transferente, Adquirente, Compartidos];
}
