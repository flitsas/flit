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
    private volatile IReadOnlyDictionary<string, FurTemplateFormat>? _cache;

    public VehicleClassificationFurResolver(
        IServiceScopeFactory scopeFactory,
        ILogger<VehicleClassificationFurResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<FurTemplateFormat> ResolveAsync(string? vehicleClass, CancellationToken ct = default)
    {
        var map = await GetMapAsync(ct).ConfigureAwait(false);
        var format = FurTemplateResolution.Resolve(vehicleClass, map);

        // Observabilidad (D2): dejar traza cuando una clasificación no matchea y cae al default.
        if (format == FurTemplateFormat.Automotor
            && !string.IsNullOrWhiteSpace(vehicleClass)
            && !map.ContainsKey(FurClassificationNormalizer.Normalize(vehicleClass)))
        {
            LogSinMatch(vehicleClass);
        }

        return format;
    }

    private async Task<IReadOnlyDictionary<string, FurTemplateFormat>> GetMapAsync(CancellationToken ct)
    {
        var cached = _cache;
        if (cached is not null)
            return cached;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
            var rows = await db.Database
                .SqlQueryRaw<VehicleClassificationFurRow>(
                    "SELECT classification AS \"Classification\", template_format AS \"TemplateFormat\" "
                    + "FROM tramites.vehicle_classification_fur WHERE deleted_at IS NULL")
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var dict = new Dictionary<string, FurTemplateFormat>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var key = FurClassificationNormalizer.Normalize(r.Classification);
                if (key.Length > 0 && FurTemplateResolution.TryParseFormat(r.TemplateFormat, out var fmt))
                    dict[key] = fmt;
            }

            _cache = dict; // se cachea solo cuando la lectura fue exitosa (una carrera es inocua: idempotente).
            return dict;
        }
        catch (Exception ex)
        {
            // Degradación segura: sin catálogo, todo cae a AUTOMOTOR. NO se cachea el vacío (reintenta luego).
            LogCatalogoNoDisponible(ex);
            return new Dictionary<string, FurTemplateFormat>(StringComparer.Ordinal);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "FUR: clasificación '{vehicleClass}' sin equivalencia en el catálogo → plantilla AUTOMOTOR (default)")]
    private partial void LogSinMatch(string vehicleClass);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FUR: no se pudo leer el catálogo de clasificación; se usará AUTOMOTOR por defecto")]
    private partial void LogCatalogoNoDisponible(Exception ex);

    private sealed record VehicleClassificationFurRow(string Classification, string TemplateFormat);
}
