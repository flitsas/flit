using System.Globalization;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Filtros de <c>GET /diagnosticos</c> ya validados (HU #13845). <see cref="Desde"/> y <see cref="Hasta"/> son instantes UTC:
/// el inicio del día <c>desde</c> y el inicio del día siguiente a <c>hasta</c>, ambos en hora Colombia (rango semiabierto).
/// </summary>
internal sealed record FiltroListado(
    DateTimeOffset? Desde, DateTimeOffset? Hasta, ModoDiagnostico? Modo, EstadoDiagnostico? Estado, string? Nombre, int Pagina, int Tamano)
{
    public const string CodigoRangoInvalido = "RANGO_INVALIDO";
    public const string CodigoFiltroInvalido = "FILTRO_INVALIDO";
    public const string CodigoPaginacionInvalida = "PAGINACION_INVALIDA";
    public const int TamanoPorDefecto = 25;
    public const int TamanoMaximo = 100;
    public const int NombreMaximo = 100;

    /// <summary>Estados por los que se puede filtrar: un lote sin lanzar (CREADO) solo aparece sin filtro de estado (F08-C5).</summary>
    public static readonly IReadOnlyList<EstadoDiagnostico> EstadosFiltrables = [EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, EstadoDiagnostico.Incompleto];

    /// <summary>
    /// Lee la query tal cual llega. 400 con <c>campo</c>: <c>FILTRO_INVALIDO</c> (fecha que no es AAAA-MM-DD, modo o estado
    /// desconocido, nombre de más de <see cref="NombreMaximo"/> caracteres), <c>RANGO_INVALIDO</c> (<c>hasta</c> antes de
    /// <c>desde</c>) o <c>PAGINACION_INVALIDA</c> (<c>pagina</c> &lt; 1, <c>tamano</c> fuera de 1..<see cref="TamanoMaximo"/>).
    /// </summary>
    public static FiltroListado Leer(string? desde, string? hasta, string? modo, string? estado, string? nombre, string? pagina, string? tamano)
    {
        var diaDesde = Dia(desde, "desde");
        var diaHasta = Dia(hasta, "hasta");
        if (diaDesde is { } d && diaHasta is { } h && h < d)
            throw Invalido(CodigoRangoInvalido, "hasta no puede ser anterior a desde.", "hasta");

        ModoDiagnostico? filtroModo = null;
        if (!string.IsNullOrEmpty(modo))
        {
            if (!CodigosDeEnum.TryLeer<ModoDiagnostico>(modo, out var m))
                throw Invalido(CodigoFiltroInvalido, "modo es SIMPLE o CON_ANALISIS.", "modo");
            filtroModo = m;
        }

        EstadoDiagnostico? filtroEstado = null;
        if (!string.IsNullOrEmpty(estado))
        {
            if (!CodigosDeEnum.TryLeer<EstadoDiagnostico>(estado, out var e) || !EstadosFiltrables.Contains(e))
                throw Invalido(CodigoFiltroInvalido, "estado es EN_PROCESO, COMPLETADO o INCOMPLETO.", "estado");
            filtroEstado = e;
        }

        var texto = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        if (texto is { Length: > NombreMaximo })
            throw Invalido(CodigoFiltroInvalido, $"nombre admite hasta {NombreMaximo} caracteres.", "nombre");

        var numero = Entero(pagina, "pagina", 1);
        if (numero < 1)
            throw Invalido(CodigoPaginacionInvalida, "pagina empieza en 1.", "pagina");
        var porPagina = Entero(tamano, "tamano", TamanoPorDefecto);
        if (porPagina is < 1 or > TamanoMaximo)
            throw Invalido(CodigoPaginacionInvalida, $"tamano va de 1 a {TamanoMaximo}.", "tamano");

        return new FiltroListado(Inicio(diaDesde), Inicio(diaHasta?.AddDays(1)), filtroModo, filtroEstado, texto, numero, porPagina);
    }

    /// <summary>Patrón de <c>ILIKE</c> «contiene» con <c>\</c>, <c>%</c> y <c>_</c> como literales (escape <c>\</c>).</summary>
    public static string PatronContiene(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return "%" + texto.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
    }

    private static DateOnly? Dia(string? valor, string campo)
    {
        if (string.IsNullOrEmpty(valor))
            return null;
        return DateOnly.TryParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw Invalido(CodigoFiltroInvalido, $"{campo} es una fecha AAAA-MM-DD.", campo);
    }

    private static int Entero(string? valor, string campo, int porDefecto)
    {
        if (string.IsNullOrEmpty(valor))
            return porDefecto;
        return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw Invalido(CodigoPaginacionInvalida, $"{campo} es un número entero.", campo);
    }

    /// <summary>00:00 hora Colombia (UTC−5 fijo, <see cref="VigenciaDelDia.OffsetColombia"/>) del día, como instante UTC.</summary>
    private static DateTimeOffset? Inicio(DateOnly? dia) =>
        dia is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), VigenciaDelDia.OffsetColombia).ToUniversalTime() : null;

    private static ReglaException Invalido(string codigo, string mensaje, string campo) =>
        new(codigo, mensaje, StatusCodes.Status400BadRequest) { Campo = campo };
}

/// <summary>
/// Listado de lotes de la compañía del token (HU #13845, F08-H1, F08-C1..C6): la visibilidad es la de
/// <see cref="AccesoLote.VisiblesAsync"/> (la misma de la lectura individual) y la compañía la pone <c>TenantFilter</c>. Orden
/// <c>creado_en</c> desc e <c>id</c> desc (índice <c>ix_diag_tenant_creado</c>). El nombre del tipo de trámite sale del
/// catálogo (caché de 5 min; si no responde o el código ya no está publicado, el nombre es el código) y el del creador de
/// <see cref="IResolutorNombresUsuario"/>, solo para los creadores distintos de la página.
/// </summary>
internal sealed class ListadoDiagnosticos(DiagnosticoDb db, UsuarioActual usuario, ICatalogoTiposTramite catalogo, IResolutorNombresUsuario nombres)
{
    public async Task<ListadoDiagnosticosDto> ListarAsync(FiltroListado filtro, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var empresa = usuario.EmpresaPropia();
        var cabeceras = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>().AsNoTracking(), db, usuario, ct).ConfigureAwait(false);
        if (filtro.Desde is { } desde)
            cabeceras = cabeceras.Where(c => c.CreadoEn >= desde);
        if (filtro.Hasta is { } hasta)
            cabeceras = cabeceras.Where(c => c.CreadoEn < hasta);
        if (filtro.Modo is { } modo)
            cabeceras = cabeceras.Where(c => c.Modo == modo);
        if (filtro.Estado is { } estado)
            cabeceras = cabeceras.Where(c => c.Estado == estado);
        if (filtro.Nombre is { } nombre)
        {
            var patron = FiltroListado.PatronContiene(nombre);
            cabeceras = cabeceras.Where(c => EF.Functions.ILike(c.Nombre, patron, "\\"));
        }

        var total = await cabeceras.CountAsync(ct).ConfigureAwait(false);
        var lotes = await cabeceras
            .OrderByDescending(c => c.CreadoEn).ThenByDescending(c => c.Id)
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano)
            .Select(c => new { c.Id, c.Nombre, c.CreadoEn, c.Modo, c.TipoTramiteCodigo, c.Estado, c.UsuarioCreadorId })
            .ToListAsync(ct).ConfigureAwait(false);

        var creadores = lotes.Count == 0
            ? new Dictionary<Guid, string>()
            : await nombres.ResolverAsync(empresa, lotes.Select(l => l.UsuarioCreadorId), ct).ConfigureAwait(false);
        var tipos = lotes.Any(l => l.TipoTramiteCodigo is not null) ? await TiposAsync(ct).ConfigureAwait(false) : new Dictionary<string, string>();

        return new ListadoDiagnosticosDto(filtro.Pagina, filtro.Tamano, total, [.. lotes.Select(l => new DiagnosticoListadoDto(
            l.Id, l.Nombre, l.CreadoEn, CodigosDeEnum.Codigo(l.Modo),
            l.TipoTramiteCodigo is { } codigo ? new TipoTramiteListadoDto(codigo, tipos.GetValueOrDefault(codigo, codigo)) : null,
            CodigosDeEnum.Codigo(l.Estado),
            new UsuarioCreadorDto(l.UsuarioCreadorId, creadores.GetValueOrDefault(l.UsuarioCreadorId, ResolutorNombresUsuario.NoDisponible))))]);
    }

    /// <summary>Código → nombre de los tipos publicados en la etapa; vacío si el catálogo no está disponible (el nombre será el código).</summary>
    private async Task<IReadOnlyDictionary<string, string>> TiposAsync(CancellationToken ct)
    {
        try
        {
            var publicados = await catalogo.ListarAsync(ct).ConfigureAwait(false);
            return publicados.GroupBy(t => t.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.Ordinal);
        }
        catch (CatalogoTiposTramiteNoDisponibleException)
        {
            return new Dictionary<string, string>();
        }
    }
}
