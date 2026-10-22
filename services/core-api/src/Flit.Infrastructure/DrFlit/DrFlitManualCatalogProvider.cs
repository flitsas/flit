using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Manual;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.DrFlit;

/// <summary>
/// Carga el manual de DR. FLIT desde el artefacto generado por el frontend (HU #12921, ADR-0060 §3):
/// <c>Content/dr-flit/manual-catalog.generated.json</c> del content root, producido por
/// <c>pnpm manual:export</c> (HU #12920). Se lee una sola vez y queda en memoria.
/// <para>
/// Si el archivo falta, no parsea o trae una versión de esquema desconocida, se loguea y el catálogo
/// queda en <c>null</c>: el chat responde degradado, pero el resto de la API arranca y funciona.
/// </para>
/// </summary>
internal sealed class DrFlitManualCatalogProvider : IDrFlitManualCatalogProvider
{
    /// <summary>Ruta del artefacto relativa al content root.</summary>
    public const string DefaultRelativePath = "Content/dr-flit/manual-catalog.generated.json";

    /// <summary>Versión de esquema que entiende este lector (<c>DR_FLIT_MANUAL_CATALOG_SCHEMA_VERSION</c>).</summary>
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lazy<DrFlitManualCatalog?> _catalog;

    public DrFlitManualCatalogProvider(IHostEnvironment environment, ILogger<DrFlitManualCatalogProvider> logger)
        : this(Path.Combine(environment.ContentRootPath, DefaultRelativePath), logger)
    {
    }

    internal DrFlitManualCatalogProvider(string path, ILogger<DrFlitManualCatalogProvider> logger)
    {
        _catalog = new Lazy<DrFlitManualCatalog?>(() => Load(path, logger), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public DrFlitManualCatalog? GetCatalog() => _catalog.Value;

    private static DrFlitManualCatalog? Load(string path, ILogger logger)
    {
        if (!File.Exists(path))
        {
            DrFlitManualCatalogLog.Missing(logger, path);
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions);

            if (file?.SchemaVersion != SupportedSchemaVersion)
            {
                DrFlitManualCatalogLog.UnsupportedVersion(logger, file?.SchemaVersion ?? 0, SupportedSchemaVersion);
                return null;
            }

            var articles = (file.Articles ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Slug) && !string.IsNullOrWhiteSpace(a.Title))
                .Select(ToArticle)
                .ToList();

            if (articles.Count == 0)
            {
                DrFlitManualCatalogLog.Empty(logger);
                return null;
            }

            DrFlitManualCatalogLog.Loaded(logger, articles.Count);
            return new DrFlitManualCatalog(articles);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DrFlitManualCatalogLog.Invalid(logger, ex.GetType().Name);
            return null;
        }
    }

    private static DrFlitManualArticle ToArticle(CatalogArticle a) => new(
        a.Slug!,
        a.Title!,
        string.IsNullOrWhiteSpace(a.Href) ? $"/manual/{a.Slug}" : a.Href,
        Flatten(a),
        a.Sources?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Href))?.Href,
        a.PrimarySource,
        string.IsNullOrWhiteSpace(a.Audience) ? null : a.Audience.Trim());

    /// <summary>
    /// Aplana el artículo a texto para el prompt: audiencia, resumen, y cada bloque con su título,
    /// párrafos, viñetas y avisos; al final las fuentes. El orden es el del artefacto, así el texto es
    /// estable entre arranques y la caché de Anthropic lo reconoce.
    /// </summary>
    private static string Flatten(CatalogArticle a)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(a.Audience))
            sb.Append("aplica para: ").Append(a.Audience).Append('\n');
        if (!string.IsNullOrWhiteSpace(a.Summary))
            sb.Append(a.Summary.Trim()).Append('\n');

        foreach (var block in a.Blocks ?? [])
        {
            sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(block.Title))
                sb.Append("## ").Append(block.Title.Trim()).Append('\n');
            foreach (var p in block.Paragraphs ?? [])
                sb.Append(p.Trim()).Append('\n');
            foreach (var b in block.Bullets ?? [])
                sb.Append("- ").Append(b.Trim()).Append('\n');
            foreach (var c in block.Callouts ?? [])
            {
                sb.Append("Nota");
                if (!string.IsNullOrWhiteSpace(c.Title))
                    sb.Append(" (").Append(c.Title.Trim()).Append(')');
                sb.Append(": ").Append(c.Text?.Trim()).Append('\n');
            }
        }

        var sources = (a.Sources ?? []).Where(s => !string.IsNullOrWhiteSpace(s.Title)).ToList();
        if (sources.Count > 0)
        {
            sb.Append("\nfuentes: ");
            sb.AppendJoin("; ", sources.Select(s => string.IsNullOrWhiteSpace(s.Ref) ? s.Title : $"{s.Title} ({s.Ref})"));
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    // ── Forma del artefacto (frontend/lib/manual/dr-flit-catalog-export.ts) ──
    private sealed record CatalogFile(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("articles")] IReadOnlyList<CatalogArticle>? Articles);

    private sealed record CatalogArticle(
        string? Slug,
        string? Title,
        string? Href,
        string? Audience,
        string? Summary,
        bool PrimarySource,
        IReadOnlyList<CatalogBlock>? Blocks,
        IReadOnlyList<CatalogSource>? Sources);

    private sealed record CatalogBlock(
        string? Title,
        IReadOnlyList<string>? Paragraphs,
        IReadOnlyList<string>? Bullets,
        IReadOnlyList<CatalogCallout>? Callouts);

    private sealed record CatalogCallout(string? Title, string? Text);

    private sealed record CatalogSource(string? Title, string? Href, string? Ref);
}

/// <summary>Logging source-generated (CA1848) del catálogo del manual.</summary>
internal static partial class DrFlitManualCatalogLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "DR. FLIT: manual cargado ({ArticleCount} artículos)")]
    public static partial void Loaded(ILogger logger, int articleCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "DR. FLIT: no existe el artefacto del manual en {Path}; el chat responderá degradado. Regenerar con pnpm manual:export")]
    public static partial void Missing(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "DR. FLIT: el artefacto del manual no se pudo leer ({ErrorType}); el chat responderá degradado")]
    public static partial void Invalid(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "DR. FLIT: versión de esquema del manual {Found} no soportada (se espera {Expected}); el chat responderá degradado")]
    public static partial void UnsupportedVersion(ILogger logger, int found, int expected);

    [LoggerMessage(Level = LogLevel.Error, Message = "DR. FLIT: el artefacto del manual no trae artículos; el chat responderá degradado")]
    public static partial void Empty(ILogger logger);
}
