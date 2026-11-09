using System.Globalization;
using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.TransitOffices.PortalMarca;

namespace Flit.Admin.Application.Companies.TransitOffices.PortalMarca;

/// <summary>Error de negocio de la marca del portal: estado HTTP, código estable y motivo exacto en español.</summary>
public sealed record PortalMarcaError(int Status, string Code, string Message);

/// <summary>Resultado de un caso de uso: el valor o el error.</summary>
public sealed record PortalMarcaResultado<T>(T? Valor, PortalMarcaError? Error)
{
    public bool Ok => Error is null;

    public static PortalMarcaResultado<T> De(T valor) => new(valor, null);

    public static PortalMarcaResultado<T> Falla(int status, string code, string message) =>
        new(default, new PortalMarcaError(status, code, message));
}

/// <summary>
/// Reglas puras de la marca del portal (HU #14375/#14376): nombre y colores, completitud, contraste mínimo 4,5:1 (mismos pares
/// que <see cref="BrandAssetValidator"/>: texto sobre principal y texto sobre secundario), el principal legible como título
/// sobre el fondo del portal (3:1) y archivos de imagen.
/// </summary>
public static class PortalMarcaReglas
{
    public const double ContrasteMinimo = WcagContrast.DefaultMinRatio;
    /// <summary>WCAG 1.4.3 para títulos: el color principal se usa como texto (nombre, títulos y enlaces) en el portal.</summary>
    public const double ContrasteTitulos = 3d;

    /// <summary>Fondo de página del portal (<c>--flit-soft</c> de frontend-personanatural) en #RRGGBB.</summary>
    public const string FondoPortal = "#E6EFFC";

    public const int LogoMaxBytes = 512 * 1024;
    public const int BannerMaxBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> TipoPorExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
    };

    public static int LimiteBytes(string kind) => kind == PortalMarcaImagen.KindBanner ? BannerMaxBytes : LogoMaxBytes;

    public static string NombreImagen(string kind) => kind == PortalMarcaImagen.KindBanner ? "banner" : "logo";

    /// <summary>Formato del nombre y de los colores (al guardar el borrador). El contraste se exige al publicar.</summary>
    public static PortalMarcaError? ValidarFormato(string? platformName, BrandColors? colors)
    {
        if (platformName is not null)
        {
            var nombre = BrandingValidation.NormalizeName(platformName);
            if (!BrandingValidation.IsNameLengthValid(nombre))
                return Datos($"El nombre del portal debe tener entre {BrandingValidation.MinNameLength} y {BrandingValidation.MaxNameLength} caracteres.");
            if (BrandingValidation.HasMarkup(nombre))
                return Datos("El nombre del portal no puede contener etiquetas ni marcado.");
        }

        if (colors is not null)
        {
            foreach (var (valor, etiqueta) in new[]
            {
                (colors.Primary, "principal"), (colors.Secondary, "secundario"), (colors.OnPrimary, "del texto sobre el principal"),
            })
            {
                if (!BrandingValidation.IsValidHexColor(valor))
                    return Datos($"El color {etiqueta} debe tener el formato #RRGGBB.");
            }
        }

        return null;
    }

    /// <summary>Completitud (nombre y colores) y contraste del borrador para publicar. Logo y banner son opcionales.</summary>
    public static PortalMarcaError? ValidarParaPublicar(PortalMarcaSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var faltan = draft.MissingFields();
        if (faltan.Count > 0)
            return new PortalMarcaError(StatusCodes422, PortalMarcaErrores.Incompleta,
                $"Para publicar falta completar: {string.Join(", ", faltan.Select(NombreCampo))}.");

        if (ValidarFormato(draft.PlatformName, draft.Colors) is { } formato)
            return formato with { Status = StatusCodes422, Code = PortalMarcaErrores.Incompleta };

        var c = draft.Colors!;
        foreach (var (texto, fondo, etiqueta) in new[]
        {
            (c.OnPrimary, c.Primary, "el texto sobre el color principal y el color principal"),
            (c.OnPrimary, c.Secondary, "el texto sobre el color principal y el color secundario"),
        })
        {
            var ratio = WcagContrast.RoundedContrastRatio(texto, fondo);
            if (!WcagContrast.MeetsMinimum(ratio, ContrasteMinimo))
                return new PortalMarcaError(StatusCodes422, PortalMarcaErrores.Contraste,
                    $"El contraste entre {etiqueta} es {Decimal(ratio, "0.##")}:1 y el mínimo es {Decimal(ContrasteMinimo, "0.#")}:1. Elija colores con más contraste para publicar.");
        }

        var titulos = WcagContrast.RoundedContrastRatio(c.Primary, FondoPortal);
        if (!WcagContrast.MeetsMinimum(titulos, ContrasteTitulos))
            return new PortalMarcaError(StatusCodes422, PortalMarcaErrores.Contraste,
                $"El contraste entre el color principal y el fondo del portal es {Decimal(titulos, "0.##")}:1 y el mínimo es {Decimal(ContrasteTitulos, "0.#")}:1. El nombre y los títulos no se leerían: elija un color principal más oscuro para publicar.");

        return null;
    }

    /// <summary>Extensión declarada, tamaño y contenido real de una imagen. <c>null</c> = válida; <paramref name="tipo"/> sale con el tipo real.</summary>
    public static async Task<(PortalMarcaError? Error, string? Tipo)> ValidarImagenAsync(
        string kind, string? nombreArchivo, MemoryStream contenido, bool excedio, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var que = NombreImagen(kind);
        var limite = LimiteBytes(kind);
        var topeTexto = limite >= 1024 * 1024 ? $"{limite / (1024 * 1024)} MB" : $"{limite / 1024} KB";

        if (excedio || contenido.Length > limite)
            return (Archivo($"El {que} supera el tamaño máximo de {topeTexto}."), null);
        if (contenido.Length == 0)
            return (Archivo($"El archivo del {que} está vacío."), null);

        var extension = Path.GetExtension(nombreArchivo ?? string.Empty);
        if (!TipoPorExtension.TryGetValue(extension, out var tipoDeclarado))
            return (Archivo($"El {que} debe ser un archivo PNG, JPEG o WebP."), null);

        var tipoReal = await ImageContentTypeSniffer.DetectAsync(contenido, ct).ConfigureAwait(false);
        if (!TipoPorExtension.ContainsValue(tipoReal))
            return (Archivo($"El contenido del {que} no es una imagen PNG, JPEG o WebP válida."), null);
        if (!string.Equals(tipoReal, tipoDeclarado, StringComparison.Ordinal))
            return (Archivo($"El contenido del {que} no coincide con su extensión: el archivo es {NombreTipo(tipoReal)} y se cargó como {NombreTipo(tipoDeclarado)}."), null);

        return (null, tipoReal);
    }

    internal const int StatusCodes422 = 422;

    /// <summary>Número con coma decimal, como se escribe en los mensajes («4,5:1»).</summary>
    private static string Decimal(double valor, string formato) =>
        valor.ToString(formato, CultureInfo.InvariantCulture).Replace('.', ',');

    private static PortalMarcaError Datos(string mensaje) => new(400, PortalMarcaErrores.Datos, mensaje);

    private static PortalMarcaError Archivo(string mensaje) => new(400, PortalMarcaErrores.ArchivoInvalido, mensaje);

    private static string NombreTipo(string tipo) => tipo switch
    {
        "image/png" => "PNG",
        "image/jpeg" => "JPEG",
        "image/webp" => "WebP",
        _ => tipo,
    };

    private static string NombreCampo(string campo) => campo switch
    {
        "platformName" => "el nombre del portal",
        "colors.primary" => "el color principal",
        "colors.secondary" => "el color secundario",
        "colors.onPrimary" => "el color del texto sobre el principal",
        _ => campo,
    };
}
