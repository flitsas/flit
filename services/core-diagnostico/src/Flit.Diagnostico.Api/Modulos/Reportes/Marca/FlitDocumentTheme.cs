using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Tema documental de FLIT para los archivos de Diagnóstico (HU #13848, F07-H3): colores, página Carta, margen del membrete y
/// familias Poppins. <b>Copia</b> de <c>services/core-api/src/Flit.Infrastructure/Documents/Branding/FlitDocumentTheme.cs</c>
/// (HU #10855), recortada a lo que usa el PDF del vehículo: core-diagnostico no referencia <c>Flit.Infrastructure</c> (arrastraría
/// core-api a un microservicio con su propio slnx y Dockerfile). Si la marca cambia allí, se copia aquí. Los tokens de tabla
/// (<see cref="Turquesa"/>, <see cref="FondoFila"/>, <see cref="TextoTabla"/>, <see cref="Verde"/>) son propios del diseño v2 del
/// PDF de Diagnóstico y no existen en core-api.
/// </summary>
internal static class FlitDocumentTheme
{
    /// <summary>Azul FLIT (títulos de sección).</summary>
    public const string PrimaryBlue = "#557EFF";

    /// <summary>Azul oscuro FLIT (texto y etiquetas).</summary>
    public const string DarkNavy = "#162744";

    /// <summary>Gris de los textos secundarios (fechas de consulta, estados sin dato, pie de página).</summary>
    public const string Gris = "#6B7280";

    /// <summary>Turquesa FLIT: inicio del degradado de los encabezados de tabla (el mismo <c>#00DBD5</c> del arte del membrete).</summary>
    public const string Turquesa = "#00DBD5";

    /// <summary>Fondo lila claro de las filas de datos de las tablas y de la nota (diseño v2).</summary>
    public const string FondoFila = "#EEF4FF";

    /// <summary>Texto gris de las celdas de datos (diseño v2).</summary>
    public const string TextoTabla = "#4B5563";

    /// <summary>Verde de los estados favorables («Vigente», «Aprobada», «Pagada»).</summary>
    public const string Verde = "#7AC143";

    /// <summary>Texto sobre el degradado de los encabezados.</summary>
    public const string Blanco = "#FFFFFF";

    /// <summary>Todos los documentos de FLIT son tamaño Carta.</summary>
    public static readonly PageSize Page = PageSizes.Letter;

    /// <summary>Margen en los cuatro lados (cm). El membrete ocupa arriba y abajo el alto de este margen.</summary>
    public const float MarginCm = 2.54f;

    /// <summary>Poppins Regular/Bold (misma familia).</summary>
    public const string FontRegular = "Poppins";

    /// <summary>Poppins Medium (familia propia en el modelo de Google Fonts).</summary>
    public const string FontMedium = "Poppins Medium";
}
