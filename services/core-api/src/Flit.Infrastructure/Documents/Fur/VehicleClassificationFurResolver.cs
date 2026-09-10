using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Documents.Fur;

/// <summary>
/// Resolver de plantilla de FUR respaldado por el catálogo <c>tramites.vehicle_classification_fur</c>
/// (HU #10919, Feature #10918). Carga el catálogo una sola vez (cacheado en memoria, normalizado) y
/// resuelve <c>vehicle_class</c> → <see cref="FurTemplateFormat"/>. Sin match → AUTOMOTOR (D2). Si el
/// catálogo no se puede leer (BD no disponible), degrada a AUTOMOTOR sin romper la generación del FUR.
/// </summary>
internal sealed partial class VehicleClassificationFurResolver : IFurTemplateResolver
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VehicleClassificationFurResolver> _logger;
    private volatile IReadOnlyDictionary<string, FurClassificationMatch>? _cache;

    public VehicleClassificationFurResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<VehicleClassificationFurResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<FurTemplateFormat> ResolveAsync(string? vehicleClass, CancellationToken ct = default) =>
        (await ResolveMatchAsync(vehicleClass, ct).ConfigureAwait(false)).Format;

    public async Task<FurClassificationMatch> ResolveMatchAsync(string? vehicleClass, CancellationToken ct = default)
    {
        var map = await GetMapAsync(ct).ConfigureAwait(false);
        var match = FurTemplateResolution.ResolveMatch(vehicleClass, map);

        if (match.Format == FurTemplateFormat.Automotor
            && match.FieldToFill is null
            && !string.IsNullOrWhiteSpace(vehicleClass)
            && !map.ContainsKey(FurClassificationNormalizer.Normalize(vehicleClass)))
        {
            LogSinMatch(vehicleClass);
        }

        return match;
    }

    public async Task<IReadOnlyList<FurClassificationCatalogItem>> ListCatalogAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var rows = await db.Database
                .SqlQueryRaw<VehicleClassificationFurRow>(
                    "SELECT classification, template_format, field_to_fill "
                    + "FROM tramites.vehicle_classification_fur WHERE deleted_at IS NULL")
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Classification))
                .OrderBy(r => r.TemplateFormat, StringComparer.Ordinal)
                .ThenBy(r => r.Classification, StringComparer.Ordinal)
                .Select(r => new FurClassificationCatalogItem(
                    r.Classification.Trim(),
                    r.TemplateFormat.Trim(),
                    string.IsNullOrWhiteSpace(r.FieldToFill) ? null : r.FieldToFill.Trim()))
                .ToList();
        }
        catch (Exception ex)
        {
            LogCatalogoNoDisponible(ex);
            return [];
        }
    }

    private async Task<IReadOnlyDictionary<string, FurClassificationMatch>> GetMapAsync(CancellationToken ct)
    {
        var cached = _cache;
        if (cached is not null)
            return cached;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            // Columnas en snake_case SIN alias: el DbContext usa UseSnakeCaseNamingConvention(), así que
            // SqlQueryRaw<T> mapea Classification→classification y TemplateFormat→template_format.
            var rows = await db.Database
                .SqlQueryRaw<VehicleClassificationFurRow>(
                    "SELECT classification, template_format, field_to_fill "
                    + "FROM tramites.vehicle_classification_fur WHERE deleted_at IS NULL")
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var dict = new Dictionary<string, FurClassificationMatch>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = FurClassificationNormalizer.Normalize(r.Classification);
                if (key.Length > 0 && FurTemplateResolution.TryParseFormat(r.TemplateFormat, out var fmt))
                {
                    dict[key] = new FurClassificationMatch(
                        fmt,
                        string.IsNullOrWhiteSpace(r.FieldToFill) ? null : r.FieldToFill.Trim());
                }
            }

            // Solo se cachea un catálogo NO vacío. Un resultado de 0 filas significa que el seed aún no
            // llegó (p. ej. el proceso arrancó antes de aplicar la migración HU #10919): cachearlo dejaría
            // TODO en AUTOMOTOR de por vida del proceso (maquinaria/remolques incluidos). Dejarlo sin cachear
            // hace que reintente en la próxima consulta hasta que el catálogo esté disponible. Una carrera es
            // inocua: idempotente.
            if (dict.Count > 0)
                _cache = dict;
            return dict;
        }
        catch (Exception ex)
        {
            // Degradación segura: sin catálogo, todo cae a AUTOMOTOR. NO se cachea el vacío (reintenta luego).
            LogCatalogoNoDisponible(ex);
            return new Dictionary<string, FurClassificationMatch>(StringComparer.Ordinal);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "FUR: clasificación '{vehicleClass}' sin equivalencia en el catálogo → plantilla AUTOMOTOR (default)")]
    private partial void LogSinMatch(string vehicleClass);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FUR: no se pudo leer el catálogo de clasificación; se usará AUTOMOTOR por defecto")]
    private partial void LogCatalogoNoDisponible(Exception ex);

    private sealed record VehicleClassificationFurRow(
        string Classification,
        string TemplateFormat,
        string? FieldToFill);
}
