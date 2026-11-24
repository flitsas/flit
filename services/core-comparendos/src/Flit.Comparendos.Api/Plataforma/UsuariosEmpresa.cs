using Flit.Identidad.Grpc.V1;
using Grpc.Core;

namespace Flit.Comparendos.Api.Plataforma;

public sealed record UsuarioConPermiso(string Correo, string Nombre);

/// <summary>
/// Usuarios activos de la empresa que tienen un permiso de Comparendos, para avisos internos (S-12: la alerta sin
/// representante legal va a quienes pueden gestionarlo cuando la empresa no configuró correos de alerta). Identidad
/// devuelve los roles del producto; el permiso se resuelve con los roles por defecto del manifiesto.
/// </summary>
public interface IUsuariosEmpresa
{
    Task<IReadOnlyList<UsuarioConPermiso>> ConPermisoAsync(Guid tenant, string permiso, CancellationToken ct);

    /// <summary>
    /// Si el usuario sigue activo en la empresa con ese permiso (Reportes 2.0: el dueño de un envío programado). <c>null</c>
    /// si no se pudo saber (Identidad no responde): quien llama no debe tomarlo como un «no».
    /// <para><b>Por defecto no bloquea</b> (fail-open, devuelve sí): es lo que usan el local sin Identidad y los dobles de
    /// prueba que no lo implementan. La implementación real es <see cref="UsuariosEmpresaIdentidad"/>, la que se registra
    /// siempre que hay Identidad configurada (<c>Modulos.AddModulosComparendos</c>).</para>
    /// </summary>
    Task<bool?> TienePermisoAsync(Guid tenant, Guid usuario, string permiso, CancellationToken ct) => Task.FromResult<bool?>(true);
}

internal sealed partial class UsuariosEmpresaIdentidad(IdentidadService.IdentidadServiceClient cliente, ILogger<UsuariosEmpresaIdentidad> logger) : IUsuariosEmpresa
{
    public async Task<IReadOnlyList<UsuarioConPermiso>> ConPermisoAsync(Guid tenant, string permiso, CancellationToken ct)
    {
        var roles = Permisos.RolesPorDefecto.Where(r => r.Permisos.Contains(permiso)).Select(r => r.Codigo).ToHashSet(StringComparer.Ordinal);
        var resultado = new List<UsuarioConPermiso>();
        var pagina = string.Empty;
        try
        {
            do
            {
                var r = await cliente.ListarUsuariosEmpresaAsync(new ListarUsuariosEmpresaRequest { Producto = ProductosHabilitadosIdentidad.Producto, PageSize = 200, PageToken = pagina },
                    new Metadata { { "x-flit-tenant-id", tenant.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
                resultado.AddRange(r.Usuarios.Where(u => u.Estado == EstadoUsuario.Activo && u.Roles.Any(x => roles.Contains(x.Codigo)) && u.Email.Length > 0)
                    .Select(u => new UsuarioConPermiso(u.Email, u.Nombre)));
                pagina = r.NextPageToken;
            }
            while (pagina.Length > 0 && resultado.Count < 1000);
        }
        catch (RpcException ex)
        {
            LogNoSePudo(logger, tenant, ex.StatusCode);
        }

        return resultado;
    }

    /// <remarks>
    /// <c>Usuario.id</c> de Identidad es <c>User.Id</c>, el mismo valor que Identidad pone en el <c>sub</c> del token
    /// (<c>OidcPrincipalFactory</c>) y que <see cref="UsuarioActual.Id"/> guarda en <c>consulta_programada.creado_por</c>.
    /// </remarks>
    public async Task<bool?> TienePermisoAsync(Guid tenant, Guid usuario, string permiso, CancellationToken ct)
    {
        var roles = Permisos.RolesPorDefecto.Where(r => r.Permisos.Contains(permiso)).Select(r => r.Codigo).ToHashSet(StringComparer.Ordinal);
        try
        {
            var r = await cliente.ObtenerUsuarioAsync(new ObtenerUsuarioRequest { UsuarioId = usuario.ToString(), Producto = ProductosHabilitadosIdentidad.Producto },
                new Metadata { { "x-flit-tenant-id", tenant.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            return r.Usuario is { } u && u.Estado == EstadoUsuario.Activo && u.Roles.Any(x => roles.Contains(x.Codigo));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Identidad respondió: ya no es de la empresa.
            return false;
        }
        catch (RpcException ex)
        {
            LogNoSePudo(logger, tenant, ex.StatusCode);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudieron listar los usuarios de {Tenant} en Identidad ({Codigo}).")]
    private static partial void LogNoSePudo(ILogger logger, Guid tenant, StatusCode codigo);
}

/// <summary>Sin Identidad configurada (local): no hay a quién más avisar.</summary>
internal sealed class UsuariosEmpresaVacio : IUsuariosEmpresa
{
    public Task<IReadOnlyList<UsuarioConPermiso>> ConPermisoAsync(Guid tenant, string permiso, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UsuarioConPermiso>>([]);
}
