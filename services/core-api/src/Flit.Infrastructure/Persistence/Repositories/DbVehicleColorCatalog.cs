using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Tramites.Domain.Tramites.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Catálogo de colores desde <c>catalogs.vehicle_colors</c>.</summary>
internal sealed class DbVehicleColorCatalog : IVehicleColorCatalog
{
    private readonly FlitDbContext _context;

    public DbVehicleColorCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<VehicleColorEntry>> SearchAsync(
        string? term,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 100);
        var query = _context.Set<VehicleColor>()
            .AsNoTracking()
            .Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, $"%{t}%")
                || EF.Functions.ILike(c.Code, $"%{t}%"));
        }

        return await query
            .OrderBy(c => c.Name)
            .Take(take)
            .Select(c => new VehicleColorEntry(c.Id, c.Code, c.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VehicleColorEntry>> FindByWordsAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var palabras = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (palabras.Length == 0)
        {
            return [];
        }

        // «AZUL CLARO» → patrón «AZUL%CLARO» (sin comodines en los extremos): trae «AZUL  CLARO» (espacios
        // dobles, 57 nombres del catálogo los tienen) y candidatos como «AZUL MUY CLARO», que el llamador
        // descarta con la comparación exacta. Se escapan los comodines del texto recibido.
        var patron = string.Join('%', palabras.Select(EscaparLike));
        return await _context.Set<VehicleColor>()
            .AsNoTracking()
            .Where(c => c.IsActive && EF.Functions.ILike(c.Name, patron, "\\"))
            .OrderBy(c => c.Name)
            .Select(c => new VehicleColorEntry(c.Id, c.Code, c.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string EscaparLike(string palabra) =>
        palabra.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
