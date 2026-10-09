using System.Globalization;
using System.Text;
using Flit.Identidad.Grpc.V1;
using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Security.Application.Products;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// <c>flit.identidad.v1.IdentidadService</c> (Epic #13316, HU #13334; contrato de plataforma v1.3 §6.1): usuarios y
/// productos encendidos de una empresa, para que ningún otro servicio lea las tablas de Identidad (ADR-0064 decisión
/// 1). Todo método responde solo por la empresa de <c>x-flit-tenant-id</c>, que valida el interceptor del SDK (<see cref="PlatformServiceCallInterceptor"/>).
/// </summary>
internal sealed class IdentidadGrpcService(UsuariosEmpresaQuery usuarios, ListEnabledProductsHandler productos)
    : IdentidadService.IdentidadServiceBase
{
    /// <summary>Scope que exige cada método (contrato v1.3 §3).</summary>
    public const string Scope = "platform.identidad.read";

    internal const int PageSizeDefault = 50;
    internal const int PageSizeMax = 200;

    public override async Task<ListarUsuariosEmpresaResponse> ListarUsuariosEmpresa(ListarUsuariosEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var producto = Producto(request.Producto);
        var tomar = request.PageSize switch
        {
            < 0 => throw new RpcException(new Status(StatusCode.InvalidArgument, "page_size no puede ser negativo.")),
            0 => PageSizeDefault,
            > PageSizeMax => PageSizeMax,
            _ => request.PageSize,
        };
        var saltar = PageToken.Decode(request.PageToken);

        // Se pide uno de más para saber si hay otra página sin contar todo.
        var filas = await usuarios.ListarAsync(caller.TenantId, producto, request.IncluirNoActivos, saltar, tomar + 1, context.CancellationToken)
            .ConfigureAwait(false);
        var pagina = filas.Take(tomar).ToList();
        var roles = await usuarios.RolesAsync(caller.TenantId, [.. pagina.Select(u => u.Id)], producto, context.CancellationToken)
            .ConfigureAwait(false);

        var response = new ListarUsuariosEmpresaResponse
        {
            NextPageToken = filas.Count > tomar ? PageToken.Encode(saltar + tomar) : string.Empty,
        };
        response.Usuarios.AddRange(pagina.Select(u => ToUsuario(u, roles[u.Id])));
        return response;
    }

    public override async Task<ObtenerUsuarioResponse> ObtenerUsuario(ObtenerUsuarioRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var producto = Producto(request.Producto);
        if (!Guid.TryParse(request.UsuarioId, out var usuarioId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "usuario_id debe ser un UUID."));

        var usuario = await usuarios.ObtenerAsync(caller.TenantId, usuarioId, context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "El usuario no existe en esta empresa."));
        var roles = await usuarios.RolesAsync(caller.TenantId, [usuario.Id], producto, context.CancellationToken).ConfigureAwait(false);
        return new ObtenerUsuarioResponse { Usuario = ToUsuario(usuario, roles[usuario.Id]) };
    }

    public override async Task<ObtenerProductosHabilitadosResponse> ObtenerProductosHabilitados(ObtenerProductosHabilitadosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var response = new ObtenerProductosHabilitadosResponse();
        response.Productos.AddRange(await productos.HandleAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false));
        return response;
    }

    /// <summary>Vacío = todos; un código fuera del contrato §1 es un error del que llama.</summary>
    private static string? Producto(string producto) =>
        string.IsNullOrEmpty(producto) ? null
        : ProductCodes.All.Contains(producto, StringComparer.Ordinal) ? producto
        : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Producto desconocido: {producto}."));

    private static Usuario ToUsuario(UsuariosEmpresaQuery.UsuarioFila fila, IEnumerable<UsuariosEmpresaQuery.RolFila> roles)
    {
        var usuario = new Usuario { Id = fila.Id.ToString(), Email = fila.Email, Nombre = fila.Nombre, Estado = Estado(fila.Estado) };
        usuario.Roles.AddRange(roles.Select(r => new Rol { Id = r.Id.ToString(), Codigo = r.Codigo, Producto = r.Producto }));
        return usuario;
    }

    internal static EstadoUsuario Estado(string estado) => estado switch
    {
        "pending" => EstadoUsuario.Pendiente,
        UsuariosEmpresaQuery.EstadoActivo => EstadoUsuario.Activo,
        "inactive" => EstadoUsuario.Inactivo,
        _ => EstadoUsuario.Unspecified,
    };

    /// <summary>Token de página opaco (AIP-158). Hoy lleva el desplazamiento; el que llama no debe interpretarlo.</summary>
    internal static class PageToken
    {
        private const string Prefix = "v1:";

        public static string Encode(int offset) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Prefix + offset.ToString(CultureInfo.InvariantCulture)));

        public static int Decode(string token)
        {
            if (string.IsNullOrEmpty(token))
                return 0;
            try
            {
                var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                if (raw.StartsWith(Prefix, StringComparison.Ordinal)
                    && int.TryParse(raw.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
                    return offset;
            }
            catch (FormatException)
            {
                // Cae al error de abajo.
            }

            throw new RpcException(new Status(StatusCode.InvalidArgument, "page_token no es válido."));
        }
    }
}
