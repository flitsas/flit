using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Tema documental compartido de FLIT (HU #10855, Feature #10852). Fuente única de colores,
/// tipografía, tamaño de página y métricas de membrete para todos los generadores de PDF y para
/// el compositor del expediente. Evita la deriva visual (DRY) entre certificados, portada, pie y
/// marca de agua. Especificación del diseñador: <c>recursos dllo membrete/LEER Importante</c>.
/// </summary>
public static class FlitDocumentTheme
{
    // Colores de marca.
    /// <summary>Azul FLIT — nombre de documento (pie), código de trámite (portada).</summary>
    public const string PrimaryBlue = "#557EFF";

    /// <summary>Azul oscuro FLIT — etiqueta "TRÁMITE" y datos de la portada.</summary>
    public const string DarkNavy = "#162744";

    /// <summary>Todos los documentos de FLIT son tamaño Carta (el FUR conserva el suyo, no usa este tema).</summary>
    public static readonly PageSize Page = PageSizes.Letter;

    /// <summary>Margen en los cuatro lados (cm). El membrete ocupa arriba y abajo el alto de este margen.</summary>
    public const float MarginCm = 2.54f;

    // Pie con el nombre del documento: #557EFF, Poppins Medium 8pt, a 2,54 cm del borde derecho
    // (cualquier tamaño de hoja). El margen inferior depende del perfil de estampado de la parte
    // (ADR-0049): el resto de documentos (mandato, solicitud, compraventa, certificados) usa
    // <see cref="DocNameBottomCm"/>; las páginas del FUR usan <see cref="DocNameBottomFormularioCm"/>.
    public const float DocNameRightCm = 2.54f;
    public const float DocNameBottomCm = 1.2f;
    public const float DocNameFontSize = 8f;

    // Bug (novedad 27, nov.): a 1,2 cm del borde inferior, el sello pisaba la rejilla de "TIPO DE
    // CARROCERÍA" de la hoja 2 del FUR AUTOMOTOR (contenido oficial hasta y≈575,5pt de 612pt de alto,
    // medido con PyMuPDF sobre fur-instrucciones-p2-blank.pdf) y bandas de tinta equivalentes en las
    // demás plantillas FUR (formulario/instrucciones × automotor/maquinaria/remolques). Un fondo
    // opaco detrás del texto se descartó (ADR-0049): borra la línea inferior de la rejilla y la base
    // de las etiquetas verticales, y asume papel blanco (falso en un adjunto escaneado). En su lugar,
    // el FUR (todas sus páginas, en los tres formatos) usa un margen inferior propio de 0,56 cm —
    // dentro de la franja libre 576,0–609,2 pt medida en las cuatro geometrías FUR (ver ADR-0049) —
    // mientras el resto de documentos conserva <see cref="DocNameBottomCm"/> sin cambios.
    public const float DocNameBottomFormularioCm = 0.56f;

    // Familias tipográficas tal como se registran en QuestPDF y PdfSharpCore (ver FlitFonts).
    /// <summary>Poppins Regular/Bold (misma familia RIBBI).</summary>
    public const string FontRegular = "Poppins";

    /// <summary>Poppins Medium (familia propia en el modelo RIBBI de Google Fonts).</summary>
    public const string FontMedium = "Poppins Medium";

    /// <summary>Factor de conversión centímetros → puntos PDF (1 pt = 1/72").</summary>
    public const double CmToPt = 72d / 2.54d;

    /// <summary>Convierte centímetros a puntos PDF.</summary>
    public static double Cm(double cm) => cm * CmToPt;
}
