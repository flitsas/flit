using Flit.Diagnostico.Api.Plataforma;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Diagnostico.Api.Modulos.Usuarios;

/// <summary>
/// Rechazos del gRPC de usuarios de core-api → respuesta REST del contrato (HU #14587, contrato-api §3.4 «Mapeo de errores», D-5). El código
/// sale de <c>google.rpc.ErrorInfo</c> (<see cref="PlatformRpcErrors.Reason"/>):
/// <list type="bullet">
/// <item>PERMISSION_DENIED con <c>ROL_NO_ASIGNABLE</c> u <c>OBJETIVO_PROTEGIDO</c> → 403 con el code; sin detalle (actor sin permiso en la
/// plataforma) → 403 <b>sin cuerpo</b> (<see cref="Resultado.Prohibido"/>).</item>
/// <item>NOT_FOUND → 404: <c>EMPRESA_NO_ENCONTRADA</c> si core-api lo dice (D-5, CR-2) o si la operación no es sobre un usuario (listar,
/// invitar); si no, <c>USUARIO_NO_ENCONTRADO</c>, el mismo cuerpo con o sin detalle (SP-13).</item>
/// <item>FAILED_PRECONDITION con <c>ULTIMO_ADMINISTRADOR</c>, <c>USUARIO_SUSPENDIDO</c>, <c>INVITACION_PENDIENTE</c> o
/// <c>ROL_YA_VIGENTE</c> → 409.</item>
/// <item>INVALID_ARGUMENT: <c>ROL_NO_PERMITIDO</c> → 422 (campo rol); <c>ACCION_SOBRE_SI_MISMO</c>, <c>CORREO_INVALIDO</c> (campo correo),
/// <c>NOMBRE_REQUERIDO</c> (campo nombre), <c>FILTRO_INVALIDO</c> y <c>PAGINACION_INVALIDA</c> → 400.</item>
/// <item>Todo lo demás (UNAVAILABLE, DEADLINE_EXCEEDED, circuito abierto, token rechazado, INTERNAL, un código que no conocemos) → 503
/// <c>PLATAFORMA_NO_DISPONIBLE</c> con <c>Retry-After: 30</c>. Al log van solo el estado y el código, nunca el mensaje de core-api ni el
/// correo (SP-16).</item>
/// </list>
/// </summary>
public static partial class TraduccionErroresUsuarios
{
    public const string CodigoNoDisponible = "PLATAFORMA_NO_DISPONIBLE";
    public const string CodigoUsuarioNoEncontrado = "USUARIO_NO_ENCONTRADO";

    /// <summary>Lo que hay que responder: una regla con cuerpo o el 403 sin cuerpo.</summary>
    public sealed record Resultado(ReglaException? Regla)
    {
        public static Resultado Prohibido { get; } = new((ReglaException?)null);
    }

    private static readonly Dictionary<string, (int Status, string Mensaje, string? Campo)> Conocidos = new(StringComparer.Ordinal)
    {
        ["ROL_NO_ASIGNABLE"] = (403, "No puedes asignar ni cambiar ese rol de Diagnóstico.", null),
        ["OBJETIVO_PROTEGIDO"] = (403, "Este usuario es administrador de la compañía o de la plataforma: su acceso a Diagnóstico se gestiona en el hub.", null),
        ["ULTIMO_ADMINISTRADOR"] = (409, "La compañía se quedaría sin administrador de Diagnóstico activo.", null),
        ["USUARIO_SUSPENDIDO"] = (409, "El usuario está suspendido en Diagnóstico: reactívalo primero.", null),
        ["INVITACION_PENDIENTE"] = (409, "La invitación aún no se ha aceptado.", null),
        ["ROL_YA_VIGENTE"] = (409, "El usuario ya tiene otro rol de Diagnóstico vigente.", null),
        ["ROL_NO_PERMITIDO"] = (422, "El rol no es un rol de Diagnóstico.", "rol"),
        ["ACCION_SOBRE_SI_MISMO"] = (400, "No puedes hacer esta acción sobre tu propio usuario.", null),
        ["CORREO_INVALIDO"] = (400, "Escribe un correo válido.", "correo"),
        ["NOMBRE_REQUERIDO"] = (400, "Escribe el nombre de la persona (máximo 150 caracteres).", "nombre"),
        ["FILTRO_INVALIDO"] = (400, "Algún filtro de la búsqueda no es válido.", null),
        ["PAGINACION_INVALIDA"] = (400, "La página o el tamaño de página no son válidos.", null),
    };

    /// <summary>La regla de un código del contrato (también la usa la validación local antes de llamar).</summary>
    public static ReglaException Regla(string codigo)
    {
        var (status, mensaje, campo) = Conocidos[codigo];
        return new ReglaException(codigo, mensaje, status) { Campo = campo };
    }

    /// <summary>404 <c>USUARIO_NO_ENCONTRADO</c> (mismo cuerpo siempre, SP-13).</summary>
    public static ReglaException UsuarioNoEncontrado() =>
        new(CodigoUsuarioNoEncontrado, "El usuario no existe o no es de esta compañía.", StatusCodes.Status404NotFound);

    /// <summary>404 <c>EMPRESA_NO_ENCONTRADA</c>, el mismo cuerpo que da <c>SoloEmpresaHabilitada</c>.</summary>
    public static ReglaException EmpresaNoEncontrada() =>
        new(Backoffice.EmpresaHabilitada.CodigoNoEncontrada, "La compañía indicada no existe o no tiene Diagnóstico activo.", StatusCodes.Status404NotFound);

    /// <summary>503 <c>PLATAFORMA_NO_DISPONIBLE</c> con <c>Retry-After: 30</c>.</summary>
    public static ReglaException NoDisponible() =>
        new(CodigoNoDisponible, "No se pudo completar la operación con la plataforma. Intenta de nuevo en unos segundos.", StatusCodes.Status503ServiceUnavailable)
        {
            RetryAfterSegundos = 30,
        };

    /// <param name="sobreUsuario">true en las rutas con <c>{usuario}</c>: un NOT_FOUND sin detalle es del usuario; si no, de la empresa.</param>
    public static Resultado Traducir(RpcException ex, bool sobreUsuario, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(ex);
        ArgumentNullException.ThrowIfNull(log);
        var codigo = PlatformRpcErrors.Reason(ex);
        switch (ex.StatusCode)
        {
            case StatusCode.PermissionDenied when codigo is "ROL_NO_ASIGNABLE" or "OBJETIVO_PROTEGIDO":
                return new Resultado(Regla(codigo));
            case StatusCode.PermissionDenied:
                LogRechazo(log, ex.StatusCode.ToString(), codigo ?? "-");
                return Resultado.Prohibido;
            case StatusCode.NotFound:
                return new Resultado(codigo == Backoffice.EmpresaHabilitada.CodigoNoEncontrada || (codigo is null && !sobreUsuario)
                    ? EmpresaNoEncontrada()
                    : UsuarioNoEncontrado());
            case StatusCode.FailedPrecondition or StatusCode.InvalidArgument
                when codigo is not null && Conocidos.TryGetValue(codigo, out var conocido) && conocido.Status is not 403:
                return new Resultado(Regla(codigo));
            default:
                LogNoDisponible(log, ex.StatusCode.ToString(), codigo ?? "-");
                return new Resultado(NoDisponible());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "La plataforma rechazó al actor en usuarios de Diagnóstico ({Estado}, {Codigo}): 403 sin cuerpo.")]
    private static partial void LogRechazo(ILogger logger, string estado, string codigo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usuarios de Diagnóstico: la plataforma no respondió como se esperaba ({Estado}, {Codigo}): 503.")]
    private static partial void LogNoDisponible(ILogger logger, string estado, string codigo);
}
