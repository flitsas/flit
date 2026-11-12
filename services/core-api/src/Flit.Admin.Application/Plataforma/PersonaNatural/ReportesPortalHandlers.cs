using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Lo que el cliente pide: canales, tipos (listas; vacío = todos) y fechas (días de Bogotá, ambos incluidos). El organismo viaja en el alcance.</summary>
public sealed record SolicitudReportePortal(IReadOnlyList<string> Canales, IReadOnlyList<Guid> Tipos, DateOnly? Desde, DateOnly? Hasta);

public sealed record ReportePortalResult(PaginaReportePortal? Pagina, string? Error);

/// <summary>Escribe el Excel del reporte. Devuelve el error de alcance (el archivo se descarta) o nulo.</summary>
public interface IReportePortalExcelExporter
{
    Task<string?> ExportarAsync(
        Stream destino, AlcanceReportePortal alcance, FiltroReportePortal filtro, (int Page, int PageSize)? pagina, CancellationToken ct = default);
}

/// <summary>Validación común del listado y del Excel: el mismo filtro para los dos, para que traigan el mismo conjunto.</summary>
internal static class ReportePortalFiltros
{
    public const int PageSizeMaximo = 100;
    public const int PageSizePorDefecto = 20;

    public static (FiltroReportePortal? Filtro, string? Error) Resolver(AlcanceReportePortal alcance, SolicitudReportePortal solicitud)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        ArgumentNullException.ThrowIfNull(solicitud);

        var permitidos = alcance.Plataforma ? CanalesReportePortal.Plataforma : CanalesReportePortal.Organismo;
        var pedidos = (solicitud.Canales ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (pedidos.Any(c => !CanalesReportePortal.Plataforma.Contains(c, StringComparer.Ordinal)))
            return (null, ReportePortalErrores.CanalInvalido);

        // Un canal que el alcance no ve (aplicación para el organismo) no es error: simplemente no trae filas.
        IReadOnlyList<string> canales = pedidos.Count == 0
            ? permitidos
            : [.. pedidos.Where(c => permitidos.Contains(c, StringComparer.Ordinal))];

        if (solicitud.Desde is { } desde && solicitud.Hasta is { } hasta && desde > hasta)
            return (null, ReportePortalErrores.RangoInvalido);

        var tipos = (solicitud.Tipos ?? []).Where(t => t != Guid.Empty).Distinct().ToList();
        return (new FiltroReportePortal(canales, tipos, solicitud.Desde, solicitud.Hasta), null);
    }
}

/// <summary>HU #14397 — trámites del portal que cayeron a FLIT 2.0, según el alcance de quien consulta.</summary>
public sealed class ListarReportePortalHandler(IReportePortalReadRepository reporte)
{
    public async Task<ReportePortalResult> HandleAsync(
        AlcanceReportePortal alcance, SolicitudReportePortal solicitud, int? page, int? pageSize, CancellationToken ct = default)
    {
        var (filtro, error) = ReportePortalFiltros.Resolver(alcance, solicitud);
        if (error is not null)
            return new ReportePortalResult(null, error);

        var pagina = page ?? 1;
        var tamano = pageSize ?? ReportePortalFiltros.PageSizePorDefecto;
        if (pagina < 1 || tamano < 1 || tamano > ReportePortalFiltros.PageSizeMaximo)
            return new ReportePortalResult(null, ReportePortalErrores.PaginaInvalida);

        var (resultado, errorAlcance) = await reporte.ListarAsync(alcance, filtro!, pagina, tamano, ct).ConfigureAwait(false);
        return new ReportePortalResult(resultado, errorAlcance);
    }
}

/// <summary>
/// HU #14398 — el mismo conjunto del listado, a Excel. Sin página exporta todo lo filtrado; con página (y tamaño), solo esa página,
/// igual que el listado.
/// </summary>
public sealed class ExportarReportePortalHandler(IReportePortalExcelExporter exportador)
{
    /// <returns>El código de error, o nulo si el libro quedó escrito en <paramref name="destino"/>.</returns>
    public Task<string?> HandleAsync(
        Stream destino, AlcanceReportePortal alcance, SolicitudReportePortal solicitud, int? page, int? pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destino);
        var (filtro, error) = ReportePortalFiltros.Resolver(alcance, solicitud);
        if (error is not null)
            return Task.FromResult<string?>(error);

        (int Page, int PageSize)? pagina = null;
        if (page is not null || pageSize is not null)
        {
            var p = page ?? 1;
            var t = pageSize ?? ReportePortalFiltros.PageSizePorDefecto;
            if (p < 1 || t < 1 || t > ReportePortalFiltros.PageSizeMaximo)
                return Task.FromResult<string?>(ReportePortalErrores.PaginaInvalida);
            pagina = (p, t);
        }

        return exportador.ExportarAsync(destino, alcance, filtro!, pagina, ct);
    }
}
