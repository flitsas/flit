using System.Globalization;
using System.Net.Mail;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Usuarios;

/// <summary>
/// Usuarios de Impuestos de la compañía (plan §5.1, F5b-CF3..CF6, deltas 6 y 7 de impuestos.v1.yaml 1.1.0), delegados en
/// <c>flit.platform.v1.UsuariosProductoService</c> de core-api (<see cref="IUsuariosPlataforma"/>). Todas las rutas exigen
/// <c>impuestos.usuarios.gestionar</c> (Administrador Impuestos) o Super Admin; el Gestor recibe 403.
/// <list type="bullet">
///   <item><c>GET /usuarios</c>: <c>q</c> (≤ 100), <c>rol</c> (<c>RolImpuestos</c>), <c>estado</c> (<c>EstadoUsuarioImpuestos</c>),
///   <c>page</c>, <c>pageSize</c> (1..200; la plataforma pagina de a 100 y aquí se juntan las páginas) → <c>PaginaUsuarios</c> con
///   <c>protegido</c> (AdminCompany) y, para el Super Admin, la <c>empresa{id, nombre, nit}</c> elegida. 400 <c>FILTRO_INVALIDO</c>
///   o <c>PAGINACION_INVALIDA</c>.</item>
///   <item><c>POST /usuarios</c> (<c>{email, nombre, rol}</c>) → 201 <c>UsuarioImpuestos</c> (invitado); 400 <c>CORREO_INVALIDO</c>
///   o <c>NOMBRE_REQUERIDO</c>; 422 <c>ROL_NO_PERMITIDO</c>.</item>
///   <item><c>PUT /usuarios/{usuarioId}/rol</c>, <c>POST …/suspender</c>, <c>POST …/reactivar</c> → 204.</item>
/// </list>
/// La compañía es la del token; el Super Admin la elige con <c>?empresa</c> (<see cref="EmpresaExplicita.Opcional"/>: 403
/// <c>EMPRESA_SOLO_SUPER_ADMIN</c> para otro usuario, 400 <c>EMPRESA_REQUERIDA</c> si falta o no es válida) y debe tener Impuestos
/// (404 <c>EMPRESA_NO_ENCONTRADA</c>); sus lecturas quedan auditadas. Los rechazos de la plataforma los traduce
/// <see cref="TraduccionErroresUsuarios"/>. Nunca se registran correos.
/// </summary>
/// <example>
/// <code>
/// UsuariosEndpoints.Map(api); // api = app.MapGroup(Program.Prefijo).RequireAuthorization()
/// </code>
/// </example>
internal static class UsuariosEndpoints
{
    public const string Ruta = "/usuarios";
    public const string Recurso = "usuarios";
    public const int TamanoPorDefecto = 50;
    public const int TamanoMaximo = 200;

    /// <summary>Tamaño máximo de página de <c>ListarUsuariosProducto</c> en la plataforma.</summary>
    public const int TamanoPlataforma = 100;

    public const int LargoBusqueda = 100;

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).RequirePermiso(Permisos.UsuariosGestionar).SinCacheEnRespuesta();
        grupo.MapGet(string.Empty, ListarAsync).WithName("ListarUsuariosImpuestos");
        grupo.MapPost(string.Empty, InvitarAsync).CuerpoAcotado().WithName("InvitarUsuarioImpuestos");
        grupo.MapPut("/{usuarioId}/rol", CambiarRolAsync).CuerpoAcotado().WithName("CambiarRolUsuarioImpuestos");
        grupo.MapPost("/{usuarioId}/suspender", (string usuarioId, HttpContext http, [AsParameters] Dependencias d, CancellationToken ct) =>
            AccionAsync(usuarioId, http, d, (p, c, u) => p.SuspenderAsync(c, u, ct), ct)).WithName("SuspenderUsuarioImpuestos");
        grupo.MapPost("/{usuarioId}/reactivar", (string usuarioId, HttpContext http, [AsParameters] Dependencias d, CancellationToken ct) =>
            AccionAsync(usuarioId, http, d, (p, c, u) => p.ReactivarAsync(c, u, ct), ct)).WithName("ReactivarUsuarioImpuestos");
    }

    /// <summary>Servicios de las rutas, en un solo parámetro (<c>[AsParameters]</c>: cada uno sale del contenedor).</summary>
    internal sealed record Dependencias(
        UsuarioActual Usuario, IUsuariosPlataforma Plataforma, ServicioConfiguracion Configuracion, AuditoriaLecturaBackoffice Auditoria, AmbitoDeLaEmpresa Ambito);

    private static async Task<IResult> ListarAsync(HttpContext http, [AsParameters] Dependencias d, CancellationToken ct)
    {
        var elegida = EmpresaExplicita.Opcional(d.Usuario.EsSuperAdmin, http.Request.Query[EmpresaExplicita.Parametro]);
        var consulta = http.Request.Query;
        var busqueda = Unico(consulta["q"]);
        if (busqueda is { Length: > LargoBusqueda } || consulta["q"].Count > 1)
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.FiltroInvalido);
        var rol = Unico(consulta["rol"]);
        if (consulta["rol"].Count > 1 || (rol is not null && !RolesImpuestos.Es(rol)))
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.FiltroInvalido);
        var estadoTexto = Unico(consulta["estado"]);
        var estado = EstadosUsuarioImpuestos.DeCodigo(estadoTexto);
        if (consulta["estado"].Count > 1 || (estadoTexto is not null && estado is null))
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.FiltroInvalido);
        var page = Entero(consulta["page"], 1, 1, int.MaxValue);
        var pageSize = Entero(consulta["pageSize"], TamanoPorDefecto, 1, TamanoMaximo);

        var contexto = await ContextoAsync(d, elegida, auditar: true, ct).ConfigureAwait(false);
        var (filas, total) = await PlataformaAsync(() => PaginaAsync(d.Plataforma, contexto, busqueda, rol, estado, page, pageSize, ct)).ConfigureAwait(false);
        var empresa = elegida is { } id ? await d.Ambito.IdentificarAsync(id, ct).ConfigureAwait(false) : null;
        var items = filas.Select(UsuarioImpuestosDto.De).OfType<UsuarioImpuestosDto>().ToList();
        return Results.Ok(new PaginaUsuariosDto(items, total, page, pageSize, empresa));
    }

    private static async Task<IResult> InvitarAsync(HttpContext http, InvitacionUsuarioEntrada entrada, [AsParameters] Dependencias d, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var elegida = EmpresaExplicita.Opcional(d.Usuario.EsSuperAdmin, http.Request.Query[EmpresaExplicita.Parametro]);
        var email = entrada.Email?.Trim() ?? string.Empty;
        if (!CorreoValido(email))
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.CorreoInvalido);
        var nombre = entrada.Nombre?.Trim() ?? string.Empty;
        if (nombre.Length == 0)
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.NombreRequerido);
        if (!RolesImpuestos.Es(entrada.Rol))
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.RolNoPermitido);

        var contexto = await ContextoAsync(d, elegida, auditar: false, ct).ConfigureAwait(false);
        var pedido = new InvitarUsuarioProductoRequest { Email = email, Nombre = nombre, RolCodigo = entrada.Rol };
        await PlataformaAsync(async () =>
        {
            await d.Plataforma.InvitarAsync(contexto, pedido, ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        return Results.Json(await InvitadoAsync(d.Plataforma, contexto, email, nombre, entrada.Rol!, ct).ConfigureAwait(false), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CambiarRolAsync(string usuarioId, HttpContext http, CambioRolEntrada entrada, [AsParameters] Dependencias d, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var elegida = EmpresaExplicita.Opcional(d.Usuario.EsSuperAdmin, http.Request.Query[EmpresaExplicita.Parametro]);
        var usuario = Usuario(usuarioId);
        if (!RolesImpuestos.Es(entrada.Rol))
            throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.RolNoPermitido);
        var contexto = await ContextoAsync(d, elegida, auditar: false, ct).ConfigureAwait(false);
        await PlataformaAsync(() => d.Plataforma.CambiarRolAsync(contexto, usuario, entrada.Rol!, ct)).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> AccionAsync(
        string usuarioId, HttpContext http, Dependencias d, Func<IUsuariosPlataforma, ContextoUsuarios, Guid, Task<UsuarioProducto>> accion, CancellationToken ct)
    {
        var elegida = EmpresaExplicita.Opcional(d.Usuario.EsSuperAdmin, http.Request.Query[EmpresaExplicita.Parametro]);
        var usuario = Usuario(usuarioId);
        var contexto = await ContextoAsync(d, elegida, auditar: false, ct).ConfigureAwait(false);
        await PlataformaAsync(() => accion(d.Plataforma, contexto, usuario)).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// La compañía y el actor de la petición. Con la compañía elegida por el Super Admin: debe tener Impuestos (404) y, si es una
    /// lectura, se audita antes de leer.
    /// </summary>
    private static async Task<ContextoUsuarios> ContextoAsync(Dependencias d, Guid? elegida, bool auditar, CancellationToken ct)
    {
        var empresa = elegida ?? d.Usuario.EmpresaPropia();
        if (d.Usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario.");
        if (elegida is { } id)
        {
            await d.Configuracion.ExigirConImpuestosAsync(id, ct).ConfigureAwait(false);
            if (auditar)
                await d.Auditoria.RegistrarAsync(id, Recurso, ct).ConfigureAwait(false);
        }

        return new ContextoUsuarios(empresa, d.Usuario.Id);
    }

    /// <summary>
    /// Una página de <paramref name="pageSize"/> filas: si cabe en una página de la plataforma, una llamada; si no, las páginas de
    /// <see cref="TamanoPlataforma"/> que la cubren, en orden.
    /// </summary>
    private static async Task<(List<UsuarioProducto> Filas, int Total)> PaginaAsync(
        IUsuariosPlataforma plataforma, ContextoUsuarios contexto, string? busqueda, string? rol, EstadoUsuarioProducto? estado, int page, int pageSize, CancellationToken ct)
    {
        ListarUsuariosProductoRequest Pedido(int pagina, int tamano)
        {
            var p = new ListarUsuariosProductoRequest { Busqueda = busqueda ?? string.Empty, Pagina = pagina, Tamano = tamano };
            if (rol is not null)
                p.Roles.Add(rol);
            if (estado is { } e)
                p.Estados.Add(e);
            return p;
        }

        if (pageSize <= TamanoPlataforma)
        {
            var r = await plataforma.ListarAsync(contexto, Pedido(page, pageSize), ct).ConfigureAwait(false);
            return ([.. r.Usuarios], r.Total);
        }

        var desde = (long)(page - 1) * pageSize;
        var primera = (int)(desde / TamanoPlataforma) + 1;
        var ultima = (int)((desde + pageSize - 1) / TamanoPlataforma) + 1;
        var filas = new List<UsuarioProducto>();
        var total = 0;
        for (var pagina = primera; pagina <= ultima; pagina++)
        {
            var r = await plataforma.ListarAsync(contexto, Pedido(pagina, TamanoPlataforma), ct).ConfigureAwait(false);
            total = r.Total;
            filas.AddRange(r.Usuarios);
            if (r.Usuarios.Count < TamanoPlataforma)
                break;
        }

        var saltar = (int)(desde - ((long)(primera - 1) * TamanoPlataforma));
        return ([.. filas.Skip(saltar).Take(pageSize)], total);
    }

    /// <summary>
    /// La fila del invitado: la plataforma responde vacío y uniforme (no dice si el correo ya existía), así que se busca por correo en la
    /// compañía. Si no aparece (o la búsqueda falla), se responde con lo enviado y el id vacío.
    /// </summary>
    private static async Task<UsuarioImpuestosDto> InvitadoAsync(
        IUsuariosPlataforma plataforma, ContextoUsuarios contexto, string email, string nombre, string rol, CancellationToken ct)
    {
        if (email.Length <= LargoBusqueda)
        {
            try
            {
                var r = await plataforma.ListarAsync(contexto, new ListarUsuariosProductoRequest { Busqueda = email, Pagina = 1, Tamano = 10 }, ct).ConfigureAwait(false);
                if (r.Usuarios.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) is { } fila
                    && UsuarioImpuestosDto.De(fila) is { } dto)
                    return dto;
            }
            catch (RpcException)
            {
                // La invitación ya se hizo: sin la fila se responde con lo enviado.
            }
        }

        return new UsuarioImpuestosDto(Guid.Empty, nombre, email, rol, EstadosUsuarioImpuestos.Invitado, false);
    }

    /// <summary>Llama a la plataforma y traduce su rechazo (403 sin cuerpo si niega el permiso sin código).</summary>
    private static async Task<T> PlataformaAsync<T>(Func<Task<T>> llamada)
    {
        try
        {
            return await llamada().ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw (Exception?)TraduccionErroresUsuarios.Traducir(ex) ?? new PermisoNegadoException();
        }
    }

    private static Guid Usuario(string usuarioId) =>
        Guid.TryParse(usuarioId, out var id) && id != Guid.Empty ? id : throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.UsuarioNoEncontrado);

    private static string? Unico(StringValues valores) => StringValues.IsNullOrEmpty(valores) ? null : valores.ToString();

    private static int Entero(StringValues valores, int porDefecto, int minimo, int maximo)
    {
        if (StringValues.IsNullOrEmpty(valores))
            return porDefecto;
        return valores.Count == 1 && int.TryParse(valores.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= minimo && n <= maximo
            ? n
            : throw TraduccionErroresUsuarios.Regla(TraduccionErroresUsuarios.PaginacionInvalida);
    }

    private static bool CorreoValido(string email) =>
        email.Length is > 0 and <= 254 && MailAddress.TryCreate(email, out var correo) && string.Equals(correo.Address, email, StringComparison.Ordinal)
        && correo.Host.Contains('.', StringComparison.Ordinal);
}

/// <summary>La plataforma negó el permiso sin un código del contrato: 403 sin cuerpo (lo responde <see cref="ManejadorReglas"/>).</summary>
public sealed class PermisoNegadoException : Exception
{
    public PermisoNegadoException()
        : base("La plataforma negó el permiso.")
    {
    }

    public PermisoNegadoException(string message)
        : base(message)
    {
    }

    public PermisoNegadoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
