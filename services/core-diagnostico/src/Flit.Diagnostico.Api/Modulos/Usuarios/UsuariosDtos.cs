using System.Text.Json.Serialization;
using Flit.Platform.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Usuarios;

/// <summary>
/// Roles de Diagnóstico que este módulo acepta y muestra (contrato-api §3.3 <c>RolDiagnostico</c>, SP-10, D-5): lista blanca por código. Un
/// código distinto que llegue del gRPC no se muestra (la fila se omite) y uno del cliente es 422 <c>ROL_NO_PERMITIDO</c>.
/// </summary>
public static class RolesDiagnostico
{
    public const string Usuario = "usuario_diagnostico";
    public const string Admin = "admin_diagnostico";

    /// <summary>Código → etiqueta visible (los nombres de <c>Permisos.RolesPorDefecto</c>).</summary>
    public static IReadOnlyDictionary<string, string> Etiquetas { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Usuario] = "Usuario de Diagnóstico",
        [Admin] = "Administrador de Diagnóstico",
    };

    public static bool EsValido(string? codigo) => codigo is not null && Etiquetas.ContainsKey(codigo);
}

/// <summary><c>UsuarioDiagnostico</c> (§3.3, SP-18): solo id, tipo, nombre, correo (@pii:medium), rol de Diagnóstico, estado y acciones.</summary>
internal sealed record UsuarioDiagnosticoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("correo")] string Correo,
    [property: JsonPropertyName("rol")] string Rol,
    [property: JsonPropertyName("rol_etiqueta")] string RolEtiqueta,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("acciones")] IReadOnlyList<string> Acciones,
    [property: JsonPropertyName("motivo_sin_acciones")] string? MotivoSinAcciones)
{
    /// <summary>
    /// La fila del gRPC, o null si no se puede mostrar (D-5, defensa): id que no es uuid, rol fuera de <see cref="RolesDiagnostico"/>, tipo
    /// o estado sin valor conocido. No se revienta por una fila rara: se omite.
    /// </summary>
    public static UsuarioDiagnosticoDto? De(UsuarioProducto? fila)
    {
        if (fila is null || !Guid.TryParse(fila.Id, out var id) || !RolesDiagnostico.EsValido(fila.RolCodigo))
            return null;
        var tipo = fila.Tipo switch { TipoFila.Usuario => "USUARIO", TipoFila.Invitacion => "INVITACION", _ => null };
        var estado = fila.Estado switch
        {
            EstadoUsuarioProducto.Activo => "ACTIVO",
            EstadoUsuarioProducto.Invitado => "INVITADO",
            EstadoUsuarioProducto.Suspendido => "SUSPENDIDO",
            _ => null,
        };
        if (tipo is null || estado is null)
            return null;

        var acciones = fila.Acciones
            .Select(a => a switch
            {
                AccionUsuario.CambiarRol => "CAMBIAR_ROL",
                AccionUsuario.Suspender => "SUSPENDER",
                AccionUsuario.Reactivar => "REACTIVAR",
                _ => null,
            })
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var motivo = fila.MotivoSinAcciones switch
        {
            Flit.Platform.Grpc.V1.MotivoSinAcciones.UstedMismo => "USTED_MISMO",
            Flit.Platform.Grpc.V1.MotivoSinAcciones.AdminCompania => "ADMIN_COMPANIA",
            Flit.Platform.Grpc.V1.MotivoSinAcciones.SoloSuperadmin => "SOLO_SUPERADMIN",
            Flit.Platform.Grpc.V1.MotivoSinAcciones.Invitacion => "INVITACION",
            Flit.Platform.Grpc.V1.MotivoSinAcciones.CuentaInactiva => "CUENTA_INACTIVA",
            _ => null,
        };
        return new UsuarioDiagnosticoDto(id, tipo, fila.Nombre, fila.Email, fila.RolCodigo, RolesDiagnostico.Etiquetas[fila.RolCodigo], estado, acciones, motivo);
    }
}

/// <summary>Cabecera de la compañía del backoffice (D-6): de la misma resolución que <c>SoloEmpresaHabilitada</c>.</summary>
internal sealed record EmpresaCabeceraDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("nit")] string? Nit);

/// <summary><c>ListaUsuariosDiagnostico</c> (§3.3); <c>empresa</c> solo en el backoffice (D-6), omitida en <c>/usuarios</c>.</summary>
internal sealed record ListaUsuariosDiagnosticoDto(
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("roles_asignables")] IReadOnlyList<string> RolesAsignables,
    [property: JsonPropertyName("usuarios")] IReadOnlyList<UsuarioDiagnosticoDto> Usuarios,
    [property: JsonPropertyName("empresa"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EmpresaCabeceraDto? Empresa = null);

/// <summary>Respuesta uniforme del 202 de invitar (SP-15).</summary>
internal sealed record InvitacionAceptadaDto(
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("mensaje")] string Mensaje)
{
    public static InvitacionAceptadaDto Uniforme { get; } =
        new("ACEPTADA", "Si el correo es válido, la persona recibirá la invitación o el acceso a Diagnóstico.");
}

/// <summary><c>EmpresaUsuarios</c> (§3.3): la compañía de Identidad con sus conteos de Diagnóstico.</summary>
internal sealed record EmpresaUsuariosDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("nit")] string? Nit,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("usuarios_diagnostico")] int UsuariosDiagnostico,
    [property: JsonPropertyName("administradores_diagnostico")] int AdministradoresDiagnostico);

/// <summary><c>ListaEmpresasUsuarios</c> (§3.3).</summary>
internal sealed record ListaEmpresasUsuariosDto(
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("empresas")] IReadOnlyList<EmpresaUsuariosDto> Empresas);
