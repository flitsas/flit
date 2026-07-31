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
    double FontSizeDelta = 0);
