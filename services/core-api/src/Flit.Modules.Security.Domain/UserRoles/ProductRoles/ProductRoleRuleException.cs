namespace Flit.Modules.Security.Domain.UserRoles.ProductRoles;

/// <summary>Familia del rechazo, que el adaptador traduce a un estado gRPC (contrato §3.4).</summary>
public enum ProductRoleErrorKind
{
    InvalidArgument,
    PermissionDenied,
    NotFound,
    FailedPrecondition,
}

/// <summary>Códigos estables de rechazo de las operaciones por producto (contrato §3.3 y §3.4).</summary>
public static class ProductRoleErrorCodes
{
    public const string RolNoPermitido = "ROL_NO_PERMITIDO";
    public const string RolNoAsignable = "ROL_NO_ASIGNABLE";
    public const string ObjetivoProtegido = "OBJETIVO_PROTEGIDO";
    public const string AccionSobreSiMismo = "ACCION_SOBRE_SI_MISMO";
    public const string UsuarioNoEncontrado = "USUARIO_NO_ENCONTRADO";
    public const string UltimoAdministrador = "ULTIMO_ADMINISTRADOR";
    public const string UsuarioSuspendido = "USUARIO_SUSPENDIDO";
    public const string InvitacionPendiente = "INVITACION_PENDIENTE";
    public const string RolYaVigente = "ROL_YA_VIGENTE";
    public const string CorreoInvalido = "CORREO_INVALIDO";
    public const string NombreRequerido = "NOMBRE_REQUERIDO";
    public const string FiltroInvalido = "FILTRO_INVALIDO";
    public const string PaginacionInvalida = "PAGINACION_INVALIDA";
}

/// <summary>Rechazo de negocio de una operación por producto. <see cref="Code"/> viaja tal cual al cliente.</summary>
public sealed class ProductRoleRuleException : Exception
{
    public ProductRoleRuleException(string code, ProductRoleErrorKind kind, string message)
        : base(message)
    {
        Code = code;
        Kind = kind;
    }

    public ProductRoleRuleException()
        : this(ProductRoleErrorCodes.UsuarioNoEncontrado, ProductRoleErrorKind.NotFound, "Usuario no encontrado.")
    {
    }

    public ProductRoleRuleException(string message)
        : this(ProductRoleErrorCodes.UsuarioNoEncontrado, ProductRoleErrorKind.NotFound, message)
    {
    }

    public ProductRoleRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = ProductRoleErrorCodes.UsuarioNoEncontrado;
        Kind = ProductRoleErrorKind.NotFound;
    }

    public string Code { get; }

    public ProductRoleErrorKind Kind { get; }
}
