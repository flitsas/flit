using System.Net.Mail;
using System.Text.Json;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Grpc.V1;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Usuarios;

/// <summary>
/// Módulo Usuarios de Diagnóstico (HU #14587, contrato-api §3, ADR-0089). Diagnóstico no administra cuentas: cada ruta valida la forma,
/// la lista blanca de roles y la auto-acción, y delega en <see cref="IUsuariosPlataforma"/> (gRPC de core-api, que vuelve a aplicar todas las
/// reglas, SP-14). Las mismas cinco rutas se mapean en dos grupos:
/// <list type="bullet">
/// <item><c>/usuarios*</c> (<see cref="Map"/>): <c>diagnostico.usuarios.gestionar</c> (o SuperAdmin) en cada ruta (SP-17) y la compañía
/// <b>solo del token</b>: un parámetro <c>empresa</c> es 400 <c>EMPRESA_NO_PERMITIDA</c> (SP-13).</item>
/// <item><c>/backoffice/empresas/{empresa}/usuarios*</c> (<c>UsuariosBackofficeEndpoints</c>): solo SuperAdmin y la compañía de la ruta.</item>
/// </list>
/// <list type="bullet">
/// <item><c>GET</c> — lista paginada (<c>busqueda</c> ≤ 100, <c>rol</c> y <c>estado</c> repetibles, <c>pagina</c> ≥ 1, <c>tamano</c> 1..100,
/// <c>pagina × tamano ≤ 10 000</c>).</item>
/// <item><c>POST /invitaciones</c> — 202 uniforme (SP-15) con la política de límite <see cref="PoliticaLimiteInvitaciones"/> (DM-2). Solo el
/// código de rol viaja; <c>rol_id</c> o cualquier otro campo se ignora (SP-10). El correo nunca va a los logs (SP-16).</item>
/// <item><c>PUT /{usuario}/rol</c>, <c>POST|DELETE /{usuario}/suspension</c> — la fila actualizada.</item>
/// </list>
/// Antes de llamar: un actor que no es SuperAdmin no da <c>admin_diagnostico</c> (403 <c>ROL_NO_ASIGNABLE</c>, SP-11 = a) y nadie actúa sobre
/// sí mismo (400 <c>ACCION_SOBRE_SI_MISMO</c>, SP-12).
/// </summary>
public static partial class UsuariosEndpoints
{
    public const string Grupo = "/usuarios";
    public const string RutaInvitaciones = "/invitaciones";
    public const string RutaRol = "/{usuario:guid}/rol";
    public const string RutaSuspension = "/{usuario:guid}/suspension";

    /// <summary>
    /// Política de límite de invitar (DM-2): 20/h por actor y 100/h por compañía, 429 <c>DEMASIADAS_SOLICITUDES</c>. La registra be1-b en
    /// <c>AddRateLimiter</c>; este módulo solo la exige en la ruta.
    /// </summary>
    public const string PoliticaLimiteInvitaciones = "diagnostico-invitaciones";

    public const string CodigoEmpresaNoPermitida = "EMPRESA_NO_PERMITIDA";

    private const int MaxBusqueda = 100;
    private const int MaxTamano = 100;
    private const int TamanoPorDefecto = 25;
    private const int MaxVentana = 10_000;
    private const int MaxCorreo = 254;
    private const int MaxNombre = 150;

    /// <summary>Puerto del módulo (TryAdd: las pruebas ponen sus dobles de los clientes gRPC antes).</summary>
    public static IServiceCollection AddUsuarios(this IServiceCollection services, IConfiguration configuration) =>
        services.AddUsuariosPlataforma(configuration);

    /// <summary>Grupo de la compañía: el permiso en cada ruta y la compañía del token.</summary>
    internal static void Map(RouteGroupBuilder api)
    {
        var grupo = api.MapGroup(Grupo).RequirePermiso(Permisos.UsuariosGestionar).AddEndpointFilter(SinEmpresaExplicita);
        MapRutas(grupo, static (_, usuario) => usuario.EmpresaPropia(), "Diagnostico", conCabecera: false);
    }

    /// <summary>Las cinco rutas en <paramref name="grupo"/>; <paramref name="empresa"/> resuelve la compañía objetivo de cada petición.</summary>
    internal static void MapRutas(RouteGroupBuilder grupo, Func<HttpContext, UsuarioActual, Guid> empresa, string sufijo, bool conCabecera)
    {
        grupo.MapGet(string.Empty, (HttpContext http, UsuarioActual u, IUsuariosPlataforma p, ILoggerFactory l, CancellationToken ct) =>
                ListarAsync(http, Contexto(http, u, empresa), p, l, conCabecera, ct))
            .WithName("ListarUsuarios" + sufijo);
        grupo.MapPost(RutaInvitaciones, (HttpContext http, UsuarioActual u, IUsuariosPlataforma p, ILoggerFactory l, CancellationToken ct) =>
                InvitarAsync(http, u, Contexto(http, u, empresa), p, l, ct))
            .CuerpoAcotado().RequireRateLimiting(PoliticaLimiteInvitaciones).WithName("InvitarUsuario" + sufijo);
        grupo.MapPut(RutaRol, (Guid usuario, HttpContext http, UsuarioActual u, IUsuariosPlataforma p, ILoggerFactory l, CancellationToken ct) =>
                CambiarRolAsync(http, u, Contexto(http, u, empresa), usuario, p, l, ct))
            .CuerpoAcotado().WithName("CambiarRolUsuario" + sufijo);
        grupo.MapPost(RutaSuspension, (Guid usuario, HttpContext http, UsuarioActual u, IUsuariosPlataforma p, ILoggerFactory l, CancellationToken ct) =>
                AccionAsync(Contexto(http, u, empresa), usuario, l, "Suspender", (c, id, t) => p.SuspenderAsync(c, id, t), ct))
            .WithName("SuspenderUsuario" + sufijo);
        grupo.MapDelete(RutaSuspension, (Guid usuario, HttpContext http, UsuarioActual u, IUsuariosPlataforma p, ILoggerFactory l, CancellationToken ct) =>
                AccionAsync(Contexto(http, u, empresa), usuario, l, "Reactivar", (c, id, t) => p.ReactivarAsync(c, id, t), ct))
            .WithName("ReactivarUsuario" + sufijo);
    }

    private static ContextoUsuarios Contexto(HttpContext http, UsuarioActual usuario, Func<HttpContext, UsuarioActual, Guid> empresa) =>
        new(empresa(http, usuario), usuario.Id);

    /// <summary>§3.2: en el grupo de la compañía la empresa sale solo del token; un <c>?empresa=</c> es 400 y no se llama a nadie.</summary>
    private static async ValueTask<object?> SinEmpresaExplicita(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Request.Query.ContainsKey(Comun.EmpresaExplicita.Parametro))
        {
            return Results.Json(new { code = CodigoEmpresaNoPermitida, error = "La compañía sale de tu sesión: quita el parámetro empresa." },
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async Task<IResult> ListarAsync(
        HttpContext http, ContextoUsuarios contexto, IUsuariosPlataforma plataforma, ILoggerFactory logs, bool conCabecera, CancellationToken ct)
    {
        var consulta = http.Request.Query;
        var busqueda = Unico(consulta, "busqueda", "FILTRO_INVALIDO")?.Trim() ?? string.Empty;
        if (busqueda.Length > MaxBusqueda)
            throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO");
        var roles = consulta["rol"].Select(r => r?.Trim()).ToList();
        if (roles.Any(r => !RolesDiagnostico.EsValido(r)))
            throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO");
        var estados = new List<EstadoUsuarioProducto>();
        foreach (var e in consulta["estado"])
        {
            estados.Add(e?.Trim() switch
            {
                "ACTIVO" => EstadoUsuarioProducto.Activo,
                "INVITADO" => EstadoUsuarioProducto.Invitado,
                "SUSPENDIDO" => EstadoUsuarioProducto.Suspendido,
                _ => throw TraduccionErroresUsuarios.Regla("FILTRO_INVALIDO"),
            });
        }

        var (pagina, tamano) = Paginacion(consulta);
        var pedido = new ListarUsuariosProductoRequest { Busqueda = busqueda, Pagina = pagina, Tamano = tamano };
        pedido.Roles.AddRange(roles.OfType<string>().Distinct(StringComparer.Ordinal));
        pedido.Estados.AddRange(estados.Distinct());

        var r = await DelegarAsync(logs, sobreUsuario: false, () => plataforma.ListarAsync(contexto, pedido, ct)).ConfigureAwait(false);
        var filas = r.Usuarios.Select(UsuarioDiagnosticoDto.De).OfType<UsuarioDiagnosticoDto>().ToList();
        if (filas.Count != r.Usuarios.Count)
        {
            var log = logs.CreateLogger(typeof(UsuariosEndpoints));
            LogFilasOmitidas(log, r.Usuarios.Count - filas.Count, contexto.Empresa);
        }
        var asignables = r.RolesAsignables.Where(RolesDiagnostico.EsValido).Distinct(StringComparer.Ordinal).ToList();
        EmpresaCabeceraDto? cabecera = null;
        if (conCabecera)
        {
            // D-6: la misma resolución de SoloEmpresaHabilitada. Sin Identidad configurada (solo local) no hay nombre: va vacío.
            cabecera = new EmpresaCabeceraDto(contexto.Empresa, Backoffice.EmpresaHabilitada.Nombre(http) ?? string.Empty, Backoffice.EmpresaHabilitada.Nit(http));
        }

        return Results.Ok(new ListaUsuariosDiagnosticoDto(pagina, tamano, r.Total, asignables, filas, cabecera));
    }

    private static async Task<IResult> InvitarAsync(
        HttpContext http, UsuarioActual usuario, ContextoUsuarios contexto, IUsuariosPlataforma plataforma, ILoggerFactory logs, CancellationToken ct)
    {
        var cuerpo = await LeerObjetoAsync(http, ct).ConfigureAwait(false);
        var correo = Texto(cuerpo, "correo")?.Trim();
        if (string.IsNullOrEmpty(correo) || correo.Length > MaxCorreo || !EsCorreo(correo))
            throw TraduccionErroresUsuarios.Regla("CORREO_INVALIDO");
        var nombre = Texto(cuerpo, "nombre")?.Trim();
        if (string.IsNullOrEmpty(nombre) || nombre.Length > MaxNombre)
            throw TraduccionErroresUsuarios.Regla("NOMBRE_REQUERIDO");
        var rol = RolDelCuerpo(cuerpo, usuario);

        var pedido = new InvitarUsuarioProductoRequest { Email = correo, Nombre = nombre, RolCodigo = rol };
        await DelegarAsync(logs, sobreUsuario: false, async () =>
        {
            await plataforma.InvitarAsync(contexto, pedido, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        // SP-16: sin el correo; la causa real (nuevo, miembro, pendiente, de otra compañía) solo la conoce la auditoría de core-api.
        var log = logs.CreateLogger(typeof(UsuariosEndpoints));
        LogInvitacion(log, contexto.Actor, contexto.Empresa, rol);
        return Results.Json(InvitacionAceptadaDto.Uniforme, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> CambiarRolAsync(
        HttpContext http, UsuarioActual usuario, ContextoUsuarios contexto, Guid objetivo, IUsuariosPlataforma plataforma, ILoggerFactory logs, CancellationToken ct)
    {
        NoSobreSiMismo(contexto, objetivo);
        var rol = RolDelCuerpo(await LeerObjetoAsync(http, ct).ConfigureAwait(false), usuario);
        var fila = await DelegarAsync(logs, sobreUsuario: true, () => plataforma.CambiarRolAsync(contexto, objetivo, rol, ct)).ConfigureAwait(false);
        return Fila(fila, logs, contexto, "CambiarRol");
    }

    private static async Task<IResult> AccionAsync(
        ContextoUsuarios contexto, Guid objetivo, ILoggerFactory logs, string operacion,
        Func<ContextoUsuarios, Guid, CancellationToken, Task<UsuarioProducto>> accion, CancellationToken ct)
    {
        NoSobreSiMismo(contexto, objetivo);
        var fila = await DelegarAsync(logs, sobreUsuario: true, () => accion(contexto, objetivo, ct)).ConfigureAwait(false);
        return Fila(fila, logs, contexto, operacion);
    }

    /// <summary>
    /// La fila que devuelve la plataforma tras una escritura con éxito; si no se puede mostrar (D-5) se responde como no encontrada, nunca
    /// un 500. Ahí core-api ya escribió y rompió el contrato, así que va al log como Error (CR-5); en la lista solo se omite con Warning.
    /// </summary>
    private static IResult Fila(UsuarioProducto fila, ILoggerFactory logs, ContextoUsuarios contexto, string operacion)
    {
        if (UsuarioDiagnosticoDto.De(fila) is { } dto)
            return Results.Ok(dto);
        var log = logs.CreateLogger(typeof(UsuariosEndpoints));
        LogFilaNoMostrableTrasEscritura(log, operacion, contexto.Empresa);
        throw TraduccionErroresUsuarios.UsuarioNoEncontrado();
    }

    /// <summary>
    /// Llama a la plataforma y traduce sus rechazos (<see cref="TraduccionErroresUsuarios"/>). La red caída también es 503. El 403 sin
    /// cuerpo se lanza como <see cref="SinCuerpoException"/> para que el filtro del módulo lo responda.
    /// </summary>
    internal static async Task<T> DelegarAsync<T>(ILoggerFactory logs, bool sobreUsuario, Func<Task<T>> llamada)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            var resultado = TraduccionErroresUsuarios.Traducir(ex, sobreUsuario, logs.CreateLogger(typeof(TraduccionErroresUsuarios)));
            throw resultado.Regla ?? (Exception)new SinCuerpoException();
        }
        catch (HttpRequestException)
        {
            throw TraduccionErroresUsuarios.NoDisponible();
        }
    }

    private static void NoSobreSiMismo(ContextoUsuarios contexto, Guid objetivo)
    {
        if (objetivo == contexto.Actor)
            throw TraduccionErroresUsuarios.Regla("ACCION_SOBRE_SI_MISMO");
    }

    /// <summary>SP-10 / SP-11 = a: solo un código de la lista blanca; <c>admin_diagnostico</c> solo lo da el SuperAdmin.</summary>
    private static string RolDelCuerpo(JsonElement cuerpo, UsuarioActual usuario)
    {
        var rol = Texto(cuerpo, "rol")?.Trim();
        if (!RolesDiagnostico.EsValido(rol))
            throw TraduccionErroresUsuarios.Regla("ROL_NO_PERMITIDO");
        if (rol == RolesDiagnostico.Admin && !usuario.EsSuperAdmin)
            throw TraduccionErroresUsuarios.Regla("ROL_NO_ASIGNABLE");
        return rol!;
    }

    /// <summary>
    /// El cuerpo como objeto JSON (a lo sumo 64 KiB). Un cuerpo vacío, mal formado o que no es objeto cuenta como objeto vacío: responde la
    /// primera regla de campo (sin códigos nuevos).
    /// </summary>
    private static async Task<JsonElement> LeerObjetoAsync(HttpContext http, CancellationToken ct)
    {
        await using var flujo = await CuerpoAcotadoFiltro.LeerAsync(http.Request, ct).ConfigureAwait(false);
        try
        {
            if (flujo.Length > 0)
            {
                var raiz = await JsonSerializer.DeserializeAsync<JsonElement>(flujo, cancellationToken: ct).ConfigureAwait(false);
                if (raiz.ValueKind == JsonValueKind.Object)
                    return raiz;
            }
        }
        catch (JsonException)
        {
            // Se trata como vacío.
        }

        return JsonElement.Parse("{}");
    }

    private static string? Texto(JsonElement cuerpo, string campo) =>
        cuerpo.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool EsCorreo(string correo)
    {
        if (correo.Contains(' ', StringComparison.Ordinal) || correo.Count(c => c == '@') != 1)
            return false;
        try
        {
            var direccion = new MailAddress(correo);
            return string.Equals(direccion.Address, correo, StringComparison.Ordinal) && direccion.Host.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Un único valor del parámetro (repetido → <paramref name="codigo"/>); null si no viene.</summary>
    internal static string? Unico(IQueryCollection consulta, string clave, string codigo)
    {
        var valores = consulta[clave];
        if (valores.Count > 1)
            throw TraduccionErroresUsuarios.Regla(codigo);
        return valores.Count == 0 ? null : valores[0];
    }

    /// <summary><c>pagina</c> ≥ 1 (def. 1), <c>tamano</c> 1..100 (def. 25), <c>pagina × tamano ≤ 10 000</c>; si no, PAGINACION_INVALIDA.</summary>
    internal static (int Pagina, int Tamano) Paginacion(IQueryCollection consulta)
    {
        static int Entero(string? valor, int defecto) =>
            valor is null ? defecto : int.TryParse(valor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;

        var pagina = Entero(Unico(consulta, "pagina", "PAGINACION_INVALIDA"), 1);
        var tamano = Entero(Unico(consulta, "tamano", "PAGINACION_INVALIDA"), TamanoPorDefecto);
        if (pagina < 1 || tamano < 1 || tamano > MaxTamano || (long)pagina * tamano > MaxVentana)
            throw TraduccionErroresUsuarios.Regla("PAGINACION_INVALIDA");
        return (pagina, tamano);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Invitación a Diagnóstico delegada en la plataforma: actor {Actor}, compañía {Empresa}, rol {Rol} (202 uniforme).")]
    private static partial void LogInvitacion(ILogger logger, Guid actor, Guid empresa, string rol);

    [LoggerMessage(Level = LogLevel.Error, Message = "Contrato roto: {Operacion} de usuarios de Diagnóstico se aplicó en la plataforma (compañía {Empresa}) y devolvió una fila con un rol o estado fuera del contrato: se responde 404.")]
    private static partial void LogFilaNoMostrableTrasEscritura(ILogger logger, string operacion, Guid empresa);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La plataforma devolvió {Cantidad} fila(s) de usuarios de la compañía {Empresa} con un rol o estado fuera del contrato: se omiten.")]
    private static partial void LogFilasOmitidas(ILogger logger, int cantidad, Guid empresa);
}

/// <summary>403 sin cuerpo (§3.3): la plataforma rechazó al actor sin decir por qué. La responde <see cref="ManejadorReglasUsuarios"/>.</summary>
internal sealed class SinCuerpoException : Exception
{
    public SinCuerpoException()
        : base("La plataforma rechazó al actor.")
    {
    }

    public SinCuerpoException(string message)
        : base(message)
    {
    }

    public SinCuerpoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Convierte <see cref="SinCuerpoException"/> en 403 sin cuerpo.</summary>
internal sealed class ManejadorReglasUsuarios : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not SinCuerpoException)
            return ValueTask.FromResult(false);
        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        return ValueTask.FromResult(true);
    }
}
