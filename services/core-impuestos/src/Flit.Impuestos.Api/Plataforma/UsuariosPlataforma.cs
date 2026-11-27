using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Usuarios;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Impuestos.Api.Plataforma;

/// <summary>
/// Quién actúa y sobre qué compañía: <see cref="Empresa"/> es la del token o la que eligió el Super Admin; <see cref="Actor"/> es el
/// <c>sub</c> del usuario. Viajan en <c>x-flit-tenant-id</c> y <c>x-flit-actor-id</c>; core-api reverifica al actor en su base.
/// </summary>
public readonly record struct ContextoUsuarios(Guid Empresa, Guid Actor);

/// <summary>
/// Los usuarios de Impuestos de una compañía, gestionados por la plataforma (plan §5.1, forma de ADR-0089). Impuestos no tiene
/// usuarios propios: todo se delega en <c>flit.platform.v1.UsuariosProductoService</c> de core-api con <c>producto = impuestos</c>, que
/// aplica las reglas (roles del manifiesto, último administrador, objetivo protegido). Los rechazos llegan como
/// <see cref="RpcException"/>; sin plataforma configurada cada método lanza 503 <c>PLATAFORMA_NO_DISPONIBLE</c>.
/// </summary>
public interface IUsuariosPlataforma
{
    Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct);

    Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct);

    Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct);

    Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct);

    Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct);

    /// <summary>Usuarios del producto por compañía (global, solo Super Admin; sin empresa objetivo).</summary>
    Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct);
}

/// <summary>Registro del puerto y constantes del contrato gRPC de usuarios por producto.</summary>
/// <example>
/// <code>
/// Impuestos__Tramites__Address=http://core-api:8082
/// builder.Services.AddUsuariosPlataforma(builder.Configuration);
/// </code>
/// </example>
public static class UsuariosPlataforma
{
    /// <summary>Producto que el scope de <c>svc-impuestos</c> permite (core-api rechaza cualquier otro).</summary>
    public const string Producto = "impuestos";

    /// <summary>Scope de las operaciones por compañía.</summary>
    public const string Scope = "platform.usuarios.impuestos";

    /// <summary>Scope del conteo global del Super Admin.</summary>
    public const string ScopeConteos = "platform.usuarios.impuestos.conteos";

    /// <summary>Metadata con el uuid del usuario que actúa.</summary>
    public const string ActorMetadata = "x-flit-actor-id";

    /// <summary>Plazo de cada llamada en segundos (1 a 60; 10 por defecto).</summary>
    public const string ClavePlazo = "Impuestos:Usuarios:PlazoRpcSegundos";

    /// <summary>
    /// Clientes gRPC hacia el puerto gRPC de core-api que ya usan los catálogos (<see cref="CatalogosImpuestosExtensions.ClaveDireccion"/>),
    /// con el token de <c>svc-impuestos</c> (<c>aud=tramites</c>) y el scope de cada servicio. Sin dirección, el «no conectado».
    /// TryAdd: las pruebas registran su doble antes.
    /// </summary>
    public static IServiceCollection AddUsuariosPlataforma(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[CatalogosImpuestosExtensions.ClaveDireccion] is { Length: > 0 } direccion)
        {
            var plazo = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue(ClavePlazo, 10), 1, 60));
            services.AddFlitGrpcClient<UsuariosProductoService.UsuariosProductoServiceClient>(configuration, new Uri(direccion), Scope, o => o.Deadline = plazo);
            services.AddFlitGrpcClient<UsuariosProductoConteoService.UsuariosProductoConteoServiceClient>(configuration, new Uri(direccion), ScopeConteos, o => o.Deadline = plazo);
            services.TryAddScoped<IUsuariosPlataforma, UsuariosPlataformaGrpc>();
        }
        else
        {
            services.TryAddSingleton<IUsuariosPlataforma, UsuariosPlataformaNoConectada>();
        }

        return services;
    }
}

/// <summary>Adaptador real: fija <c>producto = impuestos</c> y arma la metadata de empresa y actor en cada pedido.</summary>
internal sealed class UsuariosPlataformaGrpc(
    UsuariosProductoService.UsuariosProductoServiceClient cliente,
    UsuariosProductoConteoService.UsuariosProductoConteoServiceClient conteo) : IUsuariosPlataforma
{
    public async Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        pedido.Producto = UsuariosPlataforma.Producto;
        return await cliente.ListarUsuariosProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false);
    }

    public async Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        pedido.Producto = UsuariosPlataforma.Producto;
        await cliente.InvitarUsuarioProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false);
    }

    public async Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct)
    {
        var pedido = new CambiarRolProductoRequest { Producto = UsuariosPlataforma.Producto, UsuarioId = usuario.ToString(), RolCodigo = rol };
        return (await cliente.CambiarRolProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false)).Usuario;
    }

    public async Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        var pedido = new SuspenderEnProductoRequest { Producto = UsuariosPlataforma.Producto, UsuarioId = usuario.ToString() };
        return (await cliente.SuspenderEnProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false)).Usuario;
    }

    public async Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        var pedido = new ReactivarEnProductoRequest { Producto = UsuariosPlataforma.Producto, UsuarioId = usuario.ToString() };
        return (await cliente.ReactivarEnProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false)).Usuario;
    }

    public async Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct)
    {
        var r = await conteo.ContarPorEmpresaAsync(new ContarPorEmpresaRequest { Producto = UsuariosPlataforma.Producto }, Opciones(null, actor, ct))
            .ResponseAsync.ConfigureAwait(false);
        return [.. r.Conteos];
    }

    private static CallOptions Opciones(Guid? empresa, Guid actor, CancellationToken ct)
    {
        var cabeceras = new Metadata { { UsuariosPlataforma.ActorMetadata, actor.ToString() } };
        // Explícita: el interceptor del SDK solo pone la del token si falta, y con el Super Admin la objetivo es la elegida.
        if (empresa is { } id)
            cabeceras.Add(PlatformServiceCallInterceptor.TenantMetadata, id.ToString());
        return new CallOptions(headers: cabeceras, cancellationToken: ct);
    }
}

/// <summary>Sin <c>Impuestos:Tramites:Address</c>: no hay con quién hablar; 503 <c>PLATAFORMA_NO_DISPONIBLE</c>.</summary>
internal sealed class UsuariosPlataformaNoConectada : IUsuariosPlataforma
{
    public Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct) =>
        throw TraduccionErroresUsuarios.NoDisponible();

    public Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct) => throw TraduccionErroresUsuarios.NoDisponible();

    public Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct) => throw TraduccionErroresUsuarios.NoDisponible();

    public Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct) => throw TraduccionErroresUsuarios.NoDisponible();

    public Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct) => throw TraduccionErroresUsuarios.NoDisponible();

    public Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct) => throw TraduccionErroresUsuarios.NoDisponible();
}
