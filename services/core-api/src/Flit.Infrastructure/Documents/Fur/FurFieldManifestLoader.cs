using System.Text.Json;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Fur;

public static class FurFieldManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static FurFieldManifest LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Manifest FUR inválido.");

        return new FurFieldManifest
        {
            Version = dto.Version ?? "2026-06",
            PageWidth = dto.PageWidth,
            PageHeight = dto.PageHeight,
            CoordinateOrigin = dto.CoordinateOrigin ?? "top-left",
            DefaultFont = dto.DefaultFont ?? "Arial",
            Fields = dto.Fields?.Select(MapField).ToArray() ?? [],
        };
    }

    /// <summary>Carga el manifest AUTOMOTOR embebido (compatibilidad).</summary>
    public static FurFieldManifest LoadEmbedded() => LoadEmbedded(FurTemplateFormat.Automotor);

    /// <summary>Carga el manifest embebido del formato indicado (HU #10920).</summary>
    public static FurFieldManifest LoadEmbedded(FurTemplateFormat format)
    {
        var asm = typeof(FurFieldManifestLoader).Assembly;
        var resourceName = "Flit.Infrastructure.Documents.Fur." + ManifestFileName(format);
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Recurso embebido no encontrado: {resourceName}");

        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>¿Existe el manifest embebido del formato? (para saltar formatos aún no incorporados).</summary>
    public static bool HasEmbedded(FurTemplateFormat format)
    {
        var asm = typeof(FurFieldManifestLoader).Assembly;
        var resourceName = "Flit.Infrastructure.Documents.Fur." + ManifestFileName(format);
        using var stream = asm.GetManifestResourceStream(resourceName);
        return stream is not null;
    }

    // AUTOMOTOR conserva el nombre histórico del recurso; los formatos nuevos usan sufijo.
    private static string ManifestFileName(FurTemplateFormat format) => format switch
    {
        FurTemplateFormat.Maquinaria => "fur-field-manifest.maquinaria.json",
        FurTemplateFormat.Remolques => "fur-field-manifest.remolques.json",
        _ => "fur-field-manifest.json",
    };

    private static FurFieldDefinition MapField(FieldDto f)
    {
        var type = f.Type?.ToLowerInvariant() switch
        {
            "checkbox" => FurFieldType.Checkbox,
            "multiline" => FurFieldType.Multiline,
            "image" => FurFieldType.Image,
            _ => FurFieldType.Text,
        };

        var align = f.Align?.ToLowerInvariant() switch
        {
            "center" => FurTextAlign.Center,
            "right" => FurTextAlign.Right,
            _ => FurTextAlign.Left,
        };

        return new FurFieldDefinition
        {
            Id = f.Id ?? throw new InvalidOperationException("Campo sin id en manifest FUR."),
            Page = f.Page <= 0 ? 1 : f.Page,
            Type = type,
            X = f.X,
            Y = f.Y,
            W = f.W,
            H = f.H,
            Size = f.Size > 0 ? f.Size : 9,
            FontSize = f.FontSize > 0 ? f.FontSize : 7,
            Bold = f.Bold ?? true,
            Align = align,
            AutoFit = f.AutoFit ?? false,
        };
    }

    private sealed class ManifestDto
    {
        public string? Version { get; set; }
        public double PageWidth { get; set; } = 1008;
        public double PageHeight { get; set; } = 612;
        public string? CoordinateOrigin { get; set; }
        public string? DefaultFont { get; set; }
        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class FieldDto
    {
        public string? Id { get; set; }
        public int Page { get; set; } = 1;
        public string? Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double Size { get; set; }
        public double FontSize { get; set; }
        public bool? Bold { get; set; }
        public string? Align { get; set; }
        public bool? AutoFit { get; set; }
    }
}
