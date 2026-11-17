using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Usuarios;
using Flit.Diagnostico.Api.Plataforma;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Backoffice de usuarios del SuperAdmin (HU #14587, contrato-api §3.2–§3.3, ADR-0089), con <see cref="PoliticaUsuariosBackoffice"/>:
/// <list type="bullet">
/// <item><c>/backoffice/empresas/{empresa}/usuarios*</c> — las mismas cinco rutas de <see cref="UsuariosEndpoints"/> sobre la compañía de la
/// ruta: <see cref="EmpresaHabilitada.SoloEmpresaHabilitada{TBuilder}"/> (404 <c>EMPRESA_NO_ENCONTRADA</c> sin Diagnóstico; 503 si Identidad
/// no responde) y luego <see cref="EmpresaExplicita.ConEmpresaExplicita{TBuilder}"/> (la lectura queda en <c>lecturas_backoffice</c>). El GET
/// trae además la cabecera <c>empresa</c> (D-6).</item>
/// <item><c>GET /backoffice/usuarios/empresas</c> — la tabla de compañías: las de Identidad con Diagnóstico (la lista de
/// <c>GET /backoffice/empresas</c>) con sus conteos del producto, búsqueda (nombre sin tildes o prefijo de NIT), filtro <c>usuarios=CON|SIN</c>,
/// orden <c>nombre|usuarios</c> y paginación. Sin Identidad: 503 <c>IDENTIDAD_NO_CONFIGURADA</c>; caída: 503 <c>IDENTIDAD_NO_DISPONIBLE</c>.
/// Nunca una lista vacía que parezca real.</item>
/// </list>
/// </summary>
internal static partial class UsuariosBackofficeEndpoints
{
    public const string GrupoEmpresa = BackofficeEndpoints.Grupo + "/empresas/{empresa}" + UsuariosEndpoints.Grupo;
    public const string RutaEmpresas = BackofficeEndpoints.Grupo + "/usuarios/empresas";

    public static void Map(RouteGroupBuilder api)
    {
        var porEmpresa = api.MapGroup(GrupoEmpresa).RequireAuthorization(PoliticaUsuariosBackoffice.Nombre).SoloEmpresaHabilitada().ConEmpresaExplicita();
        UsuariosEndpoints.MapRutas(porEmpresa, static (http, _) => EmpresaExplicita.De(http), "Backoffice", conCabecera: true);
        api.MapGet(RutaEmpresas, ListarEmpresasAsync).RequireAuthorization(PoliticaUsuariosBackoffice.Nombre).WithName("ListarEmpresasUsuariosBackoffice");
    }

    private static async Task<IResult> ListarEmpresasAsync(
        HttpContext http, UsuarioActual usuario, IEmpresasPlataforma identidad, IUsuariosPlataforma plataforma, ILoggerFactory logs, CancellationToken ct)
    {
        var consulta = http.Request.Query;
        var busqueda = UsuariosEndpoints.Unico(consulta, "busqueda", "FILTRO_INVALIDO")?.Trim() ?? string.Empty;
        if (busqueda.Length > 100)
            throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO");
        var filtro = UsuariosEndpoints.Unico(consulta, "usuarios", "FILTRO_INVALIDO");
        if (filtro is not (null or "CON" or "SIN"))
            throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO");
        var orden = UsuariosEndpoints.Unico(consulta, "orden", "FILTRO_INVALIDO") ?? "nombre";
        if (orden is not ("nombre" or "usuarios"))
            throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO");
        var (pagina, tamano) = UsuariosEndpoints.Paginacion(consulta);

        var empresas = await LeerIdentidadAsync(identidad, logs, ct).ConfigureAwait(false)
            ?? throw new ReglaException(EmpresasBackofficeEndpoints.CodigoNoConfigurada,
                "La lista de compañías no está disponible: el servicio no tiene configurada la conexión con Identidad.", StatusCodes.Status503ServiceUnavailable);
        var conteos = (await UsuariosEndpoints.DelegarAsync(logs, sobreUsuario: false, () => plataforma.ContarPorEmpresaAsync(usuario.Id, ct)).ConfigureAwait(false))
            .Select(c => (Valido: Guid.TryParse(c.EmpresaId, out var id), Id: id, c))
            .Where(x => x.Valido)
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.First().c);

        var filas = empresas.Select(e =>
        {
            var c = conteos.GetValueOrDefault(e.Id);
            return new EmpresaUsuariosDto(e.Id, e.Nombre, e.Nit, "ACTIVA", c?.Usuarios ?? 0, c?.Administradores ?? 0);
        });
        if (busqueda.Length > 0)
        {
            var clave = EmpresasBackofficeEndpoints.ClaveOrden(busqueda);
            var numerica = busqueda.All(char.IsAsciiDigit);
            filas = filas.Where(e => EmpresasBackofficeEndpoints.ClaveOrden(e.Nombre).Contains(clave, StringComparison.Ordinal)
                                     || (numerica && e.Nit is not null && e.Nit.StartsWith(busqueda, StringComparison.Ordinal)));
        }

        filas = filtro switch
        {
            "CON" => filas.Where(e => e.UsuariosDiagnostico > 0),
            "SIN" => filas.Where(e => e.UsuariosDiagnostico == 0),
            _ => filas,
        };
        var ordenadas = (orden == "usuarios" ? filas.OrderByDescending(e => e.UsuariosDiagnostico) : filas.OrderBy(_ => 0))
            .ThenBy(e => EmpresasBackofficeEndpoints.ClaveOrden(e.Nombre), StringComparer.Ordinal)
            .ThenBy(e => e.Id)
            .ToList();
        var pag = ordenadas.Skip((pagina - 1) * tamano).Take(tamano).ToList();
        return Results.Ok(new ListaEmpresasUsuariosDto(pagina, tamano, ordenadas.Count, pag));
    }

    private static async Task<IReadOnlyList<EmpresaPlataforma>?> LeerIdentidadAsync(IEmpresasPlataforma identidad, ILoggerFactory logs, CancellationToken ct)
    {
        try
        {
            return await identidad.ListarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (EmpresasBackofficeEndpoints.EsFallaDeIdentidad(ex) && !ct.IsCancellationRequested)
        {
            var log = logs.CreateLogger(typeof(UsuariosBackofficeEndpoints));
            var motivo = ex is RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name;
            LogIdentidadNoDisponible(log, motivo);
            throw new ReglaException(EmpresasBackofficeEndpoints.CodigoNoDisponible,
                "No se pudo leer la lista de compañías de Identidad. Intenta de nuevo en unos segundos.", StatusCodes.Status503ServiceUnavailable)
            { RetryAfterSegundos = 30 };
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer la lista de compañías de Identidad para el backoffice de usuarios ({Motivo}): 503.")]
    private static partial void LogIdentidadNoDisponible(ILogger logger, string motivo);
}
