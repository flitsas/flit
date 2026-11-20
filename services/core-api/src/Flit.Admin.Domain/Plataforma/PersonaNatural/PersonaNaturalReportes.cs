namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>
/// Canales que entran al reporte del portal (HU #14397, Feature #14396, Épica #14297). Los códigos son los de
/// <c>TramiteCanal</c> (Tramites.Domain, que este proyecto no referencia); una prueba los compara. Trámites
/// (gestor), Integración y el histórico sin canal nunca entran.
/// </summary>
public static class CanalesReportePortal
{
    public const string Web = "pn_web";
    public const string Punto = "pn_punto";
    public const string Aplicacion = "aplicacion";

    /// <summary>Lo que ve el SuperAdmin: web, punto del organismo y aplicación (cuando exista).</summary>
    public static readonly IReadOnlyList<string> Plataforma = [Web, Punto, Aplicacion];

    /// <summary>Lo que ve el organismo: web y su punto.</summary>
    public static readonly IReadOnlyList<string> Organismo = [Web, Punto];
}

/// <summary>Códigos de error estables del reporte del portal.</summary>
public static class ReportePortalErrores
{
    public const string OtroOrganismo = "PORTAL_REPORTES_OTRO_ORGANISMO";
    public const string SinOrganismo = "PORTAL_REPORTES_SIN_ORGANISMO";
    public const string OrganismoRequerido = "PORTAL_REPORTES_ORGANISMO_REQUERIDO";
    public const string CanalInvalido = "PORTAL_REPORTES_CANAL_INVALIDO";
    public const string RangoInvalido = "PORTAL_REPORTES_RANGO_INVALIDO";
    public const string PaginaInvalida = "PORTAL_REPORTES_PAGINA_INVALIDA";
    public const string FiltroInvalido = "PORTAL_REPORTES_FILTRO_INVALIDO";
}

/// <summary>
/// Quién consulta y hasta dónde llega. <see cref="Plataforma"/>: SuperAdmin desde Plataforma, ve los tres canales de todos los
/// organismos (<see cref="Organismos"/> solo filtra). Sin <see cref="Plataforma"/> y con <see cref="OtTenantId"/>: el
/// administrador del organismo, que ve el organismo de SU tenant (un <see cref="Organismos"/> distinto es «otro organismo»).
/// Sin ninguno de los dos: el SuperAdmin mirando un organismo concreto, que debe venir en <see cref="Organismos"/>.
/// </summary>
public sealed record AlcanceReportePortal(bool Plataforma, Guid? OtTenantId, IReadOnlyList<Guid> Organismos);

/// <summary>Filtros ya validados. <see cref="Canales"/> es la intersección entre lo pedido y lo que el alcance permite; vacío no trae filas.</summary>
public sealed record FiltroReportePortal(
    IReadOnlyList<string> Canales,
    IReadOnlyList<Guid> Tipos,
    DateOnly? Desde,
    DateOnly? Hasta);

/// <summary>
/// Una fila del reporte. <see cref="Modalidad"/>, <see cref="ValorTramite"/> y <see cref="ValorGestion"/> van nulos mientras no
/// exista el pago: no se calculan ni se infieren.
/// </summary>
public sealed record FilaReportePortal(
    string Canal,
    string? Organismo,
    string Tipo,
    string? Radicado,
    string Estado,
    DateTimeOffset FechaCaida,
    string? Modalidad,
    long? ValorTramite,
    long? ValorGestion);

public sealed record PaginaReportePortal(IReadOnlyList<FilaReportePortal> Items, int TotalCount, int Page, int PageSize);

/// <summary>Lectura del reporte del portal. El alcance lo aplica la implementación; el llamador no arma filtros de organismo.</summary>
public interface IReportePortalReadRepository
{
    /// <summary>Una página. <c>Error</c> no nulo: alcance rechazado (ver <see cref="ReportePortalErrores"/>), sin datos.</summary>
    Task<(PaginaReportePortal? Pagina, string? Error)> ListarAsync(
        AlcanceReportePortal alcance, FiltroReportePortal filtro, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Entrega las filas del mismo conjunto que <see cref="ListarAsync"/>, de la más reciente a la más antigua. Con
    /// <paramref name="pagina"/> solo esa página; sin ella, todo el conjunto filtrado. Devuelve el error de alcance, o nulo.
    /// </summary>
    Task<string?> RecorrerAsync(
        AlcanceReportePortal alcance, FiltroReportePortal filtro, (int Page, int PageSize)? pagina,
        Func<FilaReportePortal, CancellationToken, Task> alFila, CancellationToken ct = default);
}
