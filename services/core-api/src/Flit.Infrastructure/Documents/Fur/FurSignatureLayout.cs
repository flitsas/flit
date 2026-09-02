namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Geometría de la imagen de firma dentro de su campo del FUR (HU #11016). Pura y sin dependencia de
/// PdfSharpCore para poder testearla: el renderer solo aporta el tamaño en píxeles de la imagen.
/// </summary>
public static class FurSignatureLayout
{
    /// <summary>Sube la rúbrica 2 pt para que no pise la línea inferior del recuadro.</summary>
    public const double ImageLift = 2;

    /// <summary>Aire mínimo entre el recorte y la estampa de validación.</summary>
    public const double SidecarGap = 3;

    /// <summary>
    /// Encaja una imagen de <paramref name="imageWidth"/>×<paramref name="imageHeight"/> dentro de la
    /// caja <paramref name="maxWidth"/>×<paramref name="maxHeight"/> CONSERVANDO su relación de aspecto:
    /// se escala por el lado que primero toca el límite y nunca se amplía más allá de la caja. Antes la
    /// firma se dibujaba con el alto completo del campo, así que un PNG apaisado se estiraba y pisaba
    /// los campos vecinos del formulario.
    /// <para>Con medidas no positivas (imagen ilegible o campo degenerado) devuelve la caja tal cual:
    /// el comportamiento previo, para no romper la generación del FUR por un dato raro.</para>
    /// </summary>
    public static (double Width, double Height) Fit(
        double imageWidth, double imageHeight, double maxWidth, double maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return (maxWidth, maxHeight);

        var scale = Math.Min(maxWidth / imageWidth, maxHeight / imageHeight);
        return (imageWidth * scale, imageHeight * scale);
    }

    /// <summary>
    /// La estampa va pegada al ANCHO REAL de la imagen, no a la mitad reservada del campo: esa
    /// reserva dejaba un hueco vacío entre la rúbrica y el sello.
    /// </summary>
    public static (double ImageY, double SidecarX, double SidecarW) Place(
        double fieldX,
        double fieldY,
        double fieldW,
        double fieldH,
        double drawW,
        double drawH)
    {
        var imageY = fieldY + Math.Max(0, (fieldH - drawH) / 2) - ImageLift;
        var sidecarX = fieldX + drawW + SidecarGap;
        var sidecarW = Math.Max(0, fieldW - drawW - SidecarGap);
        return (imageY, sidecarX, sidecarW);
    }
}
