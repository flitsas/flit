using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Plataforma;
using Flit.Platform.Grpc.V1;

namespace Flit.Impuestos.Api.Modulos.Usuarios;

/// <summary>
/// Roles de producto de Impuestos (plan §5.2, enum <c>RolImpuestos</c> de impuestos.v1.yaml): los mismos códigos que el manifiesto
/// registra (<see cref="Permisos.RolesPorDefecto"/>). Cualquier otro (de otro producto, el genérico <c>usuario_impuestos</c> o un
/// rol propio de la compañía) no es un rol de Impuestos.
/// </summary>
public static class RolesImpuestos
{
    public const string Gestor = "gestor_impuestos";
    public const string Administrador = "admin_impuestos";

    public static IReadOnlyList<string> Todos { get; } = [Gestor, Administrador];

    public static bool Es(string? rol) => rol is not null && Todos.Contains(rol, StringComparer.Ordinal);
}

/// <summary>Estados de <c>EstadoUsuarioImpuestos</c> (impuestos.v1.yaml 1.1.0) y su equivalente en la plataforma.</summary>
public static class EstadosUsuarioImpuestos
{
    public const string Activo = "ACTIVO";
    public const string Invitado = "INVITADO";
    public const string Suspendido = "SUSPENDIDO";

    public static EstadoUsuarioProducto? DeCodigo(string? codigo) => codigo switch
    {
        Activo => EstadoUsuarioProducto.Activo,
        Invitado => EstadoUsuarioProducto.Invitado,
        Suspendido => EstadoUsuarioProducto.Suspendido,
        _ => null,
    };

    public static string? Codigo(EstadoUsuarioProducto estado) => estado switch
    {
        EstadoUsuarioProducto.Activo => Activo,
        EstadoUsuarioProducto.Invitado => Invitado,
        EstadoUsuarioProducto.Suspendido => Suspendido,
        _ => null,
    };
}

/// <summary>
/// <c>UsuarioImpuestos</c>. <see cref="Protegido"/> es true para el AdminCompany (D-ROL-ADMIN): la plataforma no le ofrece
/// acciones por ser administrador de la compañía y cambiar su rol, suspenderlo o reactivarlo responde 403 <c>OBJETIVO_PROTEGIDO</c>.
/// </summary>
public sealed record UsuarioImpuestosDto(Guid Id, string Nombre, string Email, string Rol, string Estado, bool Protegido)
{
    /// <summary>La fila de la plataforma, o null si no es de Impuestos (rol ajeno, estado o id desconocidos): no se expone.</summary>
    public static UsuarioImpuestosDto? De(UsuarioProducto fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        if (!Guid.TryParse(fila.Id, out var id) || !RolesImpuestos.Es(fila.RolCodigo) || EstadosUsuarioImpuestos.Codigo(fila.Estado) is not { } estado)
            return null;
        return new UsuarioImpuestosDto(id, fila.Nombre, fila.Email, fila.RolCodigo, estado, fila.MotivoSinAcciones == MotivoSinAcciones.AdminCompania);
    }
}

/// <summary><c>PaginaUsuarios</c>. <see cref="Empresa"/> va null para quien no es Super Admin.</summary>
public sealed record PaginaUsuariosDto(IReadOnlyList<UsuarioImpuestosDto> Items, int Total, int Page, int PageSize, EmpresaIdentificadaDto? Empresa);

/// <summary>Cuerpo de <c>POST /usuarios</c> (<c>InvitacionUsuario</c>). El correo es PII: <see cref="ToString"/> no lo imprime.</summary>
public sealed record InvitacionUsuarioEntrada(string? Email, string? Nombre, string? Rol)
{
    public override string ToString() => $"InvitacionUsuarioEntrada {{ Rol = {Rol} }}";
}

/// <summary>Cuerpo de <c>PUT /usuarios/{usuarioId}/rol</c>.</summary>
public sealed record CambioRolEntrada(string? Rol);
