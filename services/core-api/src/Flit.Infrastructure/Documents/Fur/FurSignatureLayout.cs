namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Perfil de geometría cuando hay exactamente 4 copropietarios en <c>vehicle_owner_signature</c>.
/// Usa el gutter izquierdo del rótulo «FIRMA DEL PROPIETARIO» y alinea rúbrica + estampa en la misma banda.
/// </summary>
public static class FourActorSignatureLayout
{
    /// <summary>Desplaza el origen X hacia la izquierda para usar el gutter del rótulo.</summary>
    public const double ExpandLeft = 50;

    /// <summary>Baja la estampa respecto al campo base.</summary>
    public const double DropY = 3;

    /// <summary>Fracción del ancho de columna reservada a la rúbrica (más espacio para el sidecar).</summary>
    public const double ImageFraction = 0.28;

    /// <summary>Aire entre rúbrica y estampa digital en columnas de 4 actores.</summary>
    public const double SidecarGap = 2;

    /// <summary>Fuente del sidecar: menor que el default para reducir truncado en columnas estrechas.</summary>
    public const double SidecarFontSize = 2.45;

    /// <summary>Sin lift vertical: la rúbrica y el sidecar comparten la misma banda horizontal.</summary>
    public const double ImageLift = 0;
}

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
        double drawH,
        bool fourActorLayout = false)
    {
        var imageLift = fourActorLayout ? FourActorSignatureLayout.ImageLift : ImageLift;
        var sidecarGap = fourActorLayout ? FourActorSignatureLayout.SidecarGap : SidecarGap;
        var imageY = fieldY + Math.Max(0, (fieldH - drawH) / 2) - imageLift;
        var sidecarX = fieldX + drawW + sidecarGap;
        var sidecarW = Math.Max(0, fieldW - drawW - sidecarGap);
        return (imageY, sidecarX, sidecarW);
    }

    /// <summary>
    /// Fracción del ancho del campo reservada a la rúbrica. En columnas estrechas (copropiedad)
    /// se deja más de la mitad para la estampa digital a la derecha.
    /// </summary>
    public static double ImageWidthCap(double fieldW, double maxImageW, bool fourActorLayout = false)
    {
        var fraction = fourActorLayout
            ? FourActorSignatureLayout.ImageFraction
            : fieldW < 140 ? 0.38 : 0.50;
        return Math.Min(maxImageW, fieldW * fraction);
    }

    /// <summary>Reparte el recuadro de firma en columnas iguales (1–4 copropietarios).</summary>
    public static (double X, double W)[] Columns(double fieldX, double fieldW, int count)
    {
        var n = Math.Clamp(count, 1, 4);
        var originX = fieldX;
        var totalW = fieldW;
        if (n == 4)
        {
            originX = fieldX - FourActorSignatureLayout.ExpandLeft;
            totalW = fieldW + FourActorSignatureLayout.ExpandLeft;
        }

        var w = totalW / n;
        var cols = new (double X, double W)[n];
        for (var i = 0; i < n; i++)
            cols[i] = (originX + i * w, w);
        return cols;
    }
}
