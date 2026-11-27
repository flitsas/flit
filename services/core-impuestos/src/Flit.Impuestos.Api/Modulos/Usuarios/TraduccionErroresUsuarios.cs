using System.Collections.Frozen;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Impuestos.Api.Modulos.Usuarios;

/// <summary>
/// Errores de <c>/usuarios*</c> con el código y el literal exactos de <c>x-flit-literales.errores</c> (impuestos.v1.yaml 1.1.0).
/// Traduce los rechazos de <c>flit.platform.v1.UsuariosProductoService</c>: el código sale de <c>google.rpc.ErrorInfo.reason</c> y el
/// estado HTTP del contrato (403 <c>ROL_NO_ASIGNABLE</c>/<c>OBJETIVO_PROTEGIDO</c>; 404 <c>USUARIO_NO_ENCONTRADO</c>/<c>EMPRESA_NO_ENCONTRADA</c>;
/// 409 <c>ULTIMO_ADMINISTRADOR</c>/<c>USUARIO_SUSPENDIDO</c>/<c>INVITACION_PENDIENTE</c>/<c>ROL_YA_VIGENTE</c>; 400 <c>ACCION_SOBRE_SI_MISMO</c>,
/// <c>CORREO_INVALIDO</c>, <c>NOMBRE_REQUERIDO</c>, <c>FILTRO_INVALIDO</c>, <c>PAGINACION_INVALIDA</c>; 422 <c>ROL_NO_PERMITIDO</c>). Un permiso
/// negado sin código (el actor no pasó la reverificación de core-api) es 403 sin cuerpo; la plataforma caída, sin respuesta o
/// con un código desconocido es 503 <c>PLATAFORMA_NO_DISPONIBLE</c> con <c>Retry-After</c>. Nunca repite el mensaje de core-api.
/// </summary>
/// <example>
/// <code>
/// catch (RpcException ex) when (TraduccionErroresUsuarios.Traducir(ex) is { } regla) { throw regla; }
/// </code>
/// </example>
internal static class TraduccionErroresUsuarios
{
    public const string RolNoPermitido = "ROL_NO_PERMITIDO";
    public const string CorreoInvalido = "CORREO_INVALIDO";
    public const string NombreRequerido = "NOMBRE_REQUERIDO";
    public const string FiltroInvalido = "FILTRO_INVALIDO";
    public const string PaginacionInvalida = "PAGINACION_INVALIDA";
    public const string UsuarioNoEncontrado = "USUARIO_NO_ENCONTRADO";
    public const string PlataformaNoDisponible = "PLATAFORMA_NO_DISPONIBLE";

    /// <summary>Segundos de <c>Retry-After</c> del 503 (ejemplo del contrato).</summary>
    public const int SegundosReintento = 30;

    /// <summary>Código → (estado HTTP, literal del contrato, campo del cuerpo).</summary>
    internal static FrozenDictionary<string, (int Status, string Literal, string? Campo)> Codigos { get; } =
        new Dictionary<string, (int, string, string?)>(StringComparer.Ordinal)
        {
            [RolNoPermitido] = (StatusCodes.Status422UnprocessableEntity, "El rol no es válido para Impuestos.", "rol"),
            ["ROL_NO_ASIGNABLE"] = (StatusCodes.Status403Forbidden, "No puedes asignar ese rol.", null),
            ["OBJETIVO_PROTEGIDO"] = (StatusCodes.Status403Forbidden, "Este usuario se gestiona desde el administrador de la plataforma.", null),
            ["ACCION_SOBRE_SI_MISMO"] = (StatusCodes.Status400BadRequest, "No puedes hacer esta acción sobre tu propio usuario.", null),
            [UsuarioNoEncontrado] = (StatusCodes.Status404NotFound, "El usuario no existe en Impuestos de esta compañía.", null),
            ["EMPRESA_NO_ENCONTRADA"] = (StatusCodes.Status404NotFound, "La compañía no existe o no tiene Impuestos.", null),
            ["USUARIO_SUSPENDIDO"] = (StatusCodes.Status409Conflict, "El usuario está suspendido en Impuestos.", null),
            ["INVITACION_PENDIENTE"] = (StatusCodes.Status409Conflict, "El usuario todavía no acepta su invitación.", null),
            ["ULTIMO_ADMINISTRADOR"] = (StatusCodes.Status409Conflict, "La compañía debe conservar al menos un Administrador Impuestos.", null),
            ["ROL_YA_VIGENTE"] = (StatusCodes.Status409Conflict, "El usuario ya tiene un rol vigente en Impuestos.", null),
            [CorreoInvalido] = (StatusCodes.Status400BadRequest, "Un correo no es válido.", "email"),
            [NombreRequerido] = (StatusCodes.Status400BadRequest, "Escribe el nombre.", "nombre"),
            [FiltroInvalido] = (StatusCodes.Status400BadRequest, "Un filtro no es válido.", null),
            [PaginacionInvalida] = (StatusCodes.Status400BadRequest, "La paginación no es válida.", null),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>La regla del <paramref name="codigo"/> con su estado, su literal y su campo.</summary>
    public static ReglaException Regla(string codigo)
    {
        var (status, literal, campo) = Codigos[codigo];
        return new ReglaException(codigo, literal, status) { Campo = campo };
    }

    /// <summary>503 <c>PLATAFORMA_NO_DISPONIBLE</c> con <c>Retry-After</c>.</summary>
    public static ReglaException NoDisponible() =>
        new(PlataformaNoDisponible, "La plataforma no responde. Intenta más tarde.", StatusCodes.Status503ServiceUnavailable) { RetryAfterSegundos = SegundosReintento };

    /// <summary>La regla del rechazo, o null si es un permiso negado sin código (403 sin cuerpo).</summary>
    public static ReglaException? Traducir(RpcException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var codigo = PlatformRpcErrors.Reason(ex);
        if (codigo is not null && Codigos.ContainsKey(codigo))
            return Regla(codigo);

        return ex.StatusCode == StatusCode.PermissionDenied && codigo is null ? null : NoDisponible();
    }
}
