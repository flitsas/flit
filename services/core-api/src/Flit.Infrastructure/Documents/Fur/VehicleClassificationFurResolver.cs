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
            // Columnas en snake_case SIN alias: el DbContext usa UseSnakeCaseNamingConvention(), así que
            // SqlQueryRaw<T> mapea la propiedad Classification→columna 'classification' y TemplateFormat→
            // 'template_format'. Aliasarlas a PascalCase (AS "TemplateFormat") hacía que EF buscara
            // 'template_format' y no lo hallara → InvalidOperationException → catch → catálogo vacío →
            // TODO caía a AUTOMOTOR (maquinaria/remolques incluidos). Sin alias, EF materializa las 96 filas.
            var rows = await db.Database
                .SqlQueryRaw<VehicleClassificationFurRow>(
                    "SELECT classification, template_format "
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
