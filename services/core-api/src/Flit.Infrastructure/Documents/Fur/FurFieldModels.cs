namespace Flit.Infrastructure.Documents.Fur;

public enum FurFieldType
{
    Text,
    Checkbox,
    Multiline,
    Image,
}

public enum FurTextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Definición de un campo superpuesto sobre la plantilla PDF blank.</summary>
public sealed class FurFieldDefinition
{
    public required string Id { get; init; }
    public int Page { get; init; } = 1;
    public FurFieldType Type { get; init; } = FurFieldType.Text;
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double Size { get; init; }
    public double FontSize { get; init; } = 7;
    public bool Bold { get; init; } = true;
    public FurTextAlign Align { get; init; } = FurTextAlign.Left;

    /// <summary>
    /// Opt-in al auto-encaje de <see cref="FurTextFitter.FitMultiline"/> (HU #11256, CF12). Solo
    /// aplica a campos <see cref="FurFieldType.Multiline"/>; en cualquier otro tipo se ignora.
    /// Default <c>false</c> a propósito: los sellos de firma (<c>vehicle_owner_signature</c> /
    /// <c>vehicle_buyer_signature</c>) también son <c>multiline</c> y NO deben medirse — aplicar el
    /// encaje a todo <c>multiline</c> los encogería, una regresión visible en el 100% de los FUR
    /// firmados. Se declara explícitamente por campo en el manifiesto (nunca por <c>Id</c> en el
    /// renderer: eso sería un string mágico invisible desde el manifiesto).
    /// </summary>
    public bool AutoFit { get; init; }

    /// <summary>
    /// HU sin ADO 2026-08-11 (tercera tanda) — piso ABSOLUTO de cuerpo para
    /// <see cref="FurTextFitter.Fit"/> (campos <see cref="FurFieldType.Text"/>), en vez del piso por
    /// defecto (65% de <see cref="FontSize"/>). Solo aplica a <c>Text</c>; en cualquier otro tipo se
    /// ignora. <c>null</c> (default) conserva el piso de siempre — es opt-in por campo, igual que
    /// <see cref="AutoFit"/>, para no afectar la calibración de ningún otro campo del FUR.
    /// </summary>
    public double? MinFontSize { get; init; }
}

/// <summary>Manifest de coordenadas para overlay FUR (origen top-left, puntos PDF).</summary>
public sealed class FurFieldManifest
{
    public string Version { get; init; } = "2026-06";
    public double PageWidth { get; init; } = 1008;
    public double PageHeight { get; init; } = 612;
    public string CoordinateOrigin { get; init; } = "top-left";
    public string DefaultFont { get; init; } = "Arial";
    public IReadOnlyList<FurFieldDefinition> Fields { get; init; } = [];
}

/// <summary>Una firma dentro del recuadro del lado (copropietarios en fila).</summary>
public sealed record FurOverlaySignatureStamp(
    byte[]? ImageBytes,
    string? ImageSidecarText,
    string? Text,
    double FontSizeDelta = 0);

/// <summary>Valor resuelto de un campo para el overlay.</summary>
/// <param name="ImageSidecarText">
/// Texto multilínea pintado a la derecha de <see cref="ImageBytes"/> (metadatos del baúl de firmas).
/// </param>
public sealed record FurFieldValue(
    string? Text,
    byte[]? ImageBytes = null,
    string? ImageSidecarText = null,
    // HU #11031 — ajuste de cuerpo respecto al tamaño declarado en el manifiesto. El sello de la
    // validación de identidad se imprime 2pt más pequeño: son cuatro líneas dentro del espacio de
    // firma y con el cuerpo del manifiesto se salían del recuadro.
    double FontSizeDelta = 0,
    /// <summary>Cuántas X apilar en un checkbox (copropietarios con el mismo tipo de documento).</summary>
    int CheckboxRepeat = 1,
    IReadOnlyList<FurOverlaySignatureStamp>? SignatureStamps = null);
