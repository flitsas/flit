using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// Quién actúa y sobre qué compañía (HU #14587, contrato-api §3.2): <see cref="Empresa"/> es la del token en <c>/usuarios*</c> o la de la
/// ruta en el backoffice; <see cref="Actor"/> es el <c>sub</c> del usuario. Viajan en <c>x-flit-tenant-id</c> y <c>x-flit-actor-id</c>, y
/// core-api reverifica al actor en su base (SP-14).
/// </summary>
public readonly record struct ContextoUsuarios(Guid Empresa, Guid Actor);

/// <summary>
/// Los usuarios de Diagnóstico de una compañía, gestionados por la plataforma (ADR-0089, contrato-api §3.4). Diagnóstico no tiene usuarios
/// propios: todo se delega en <c>flit.platform.v1.UsuariosProductoService</c> de core-api, que es quien aplica las reglas. Los rechazos
/// llegan como <see cref="RpcException"/> y los traduce <c>TraduccionErroresUsuarios</c>; sin plataforma configurada, cada método lanza 503
/// <see cref="CodigoNoConfigurada"/>.
/// </summary>
public interface IUsuariosPlataforma
{
    Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct);

    Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct);

    Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct);

    Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct);

    Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct);

    /// <summary>Conteo global por compañía (solo SuperAdmin, sin empresa objetivo).</summary>
    Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct);
}

/// <summary>Registro del puerto y constantes del contrato gRPC (§3.4).</summary>
public static class UsuariosPlataforma
{
    /// <summary>Único producto que el scope de svc-diagnostico permite.</summary>
    public const string Producto = "diagnostico";

    /// <summary>Scope de las operaciones por compañía.</summary>
    public const string Scope = "platform.usuarios.diagnostico";

    /// <summary>Scope del conteo global del SuperAdmin.</summary>
    public const string ScopeConteos = "platform.usuarios.diagnostico.conteos";

    /// <summary>Metadata con el uuid del usuario que actúa.</summary>
    public const string ActorMetadata = "x-flit-actor-id";

    public const string CodigoNoConfigurada = "PLATAFORMA_NO_CONFIGURADA";

    /// <summary>
    /// Clientes gRPC hacia el mismo puerto gRPC de core-api que usa el catálogo y la derivación (<c>Diagnostico:Tramites:Address</c>, el
    /// <c>Ict:GrpcPort</c> de core-api), con token de svc-diagnostico (<c>aud=tramites</c>) y su scope. El adaptador real se usa si hay
    /// clientes registrados (por la dirección, o los dobles de una prueba); si no, el «no conectado». TryAdd: las pruebas ponen su doble antes.
    /// </summary>
    public static IServiceCollection AddUsuariosPlataforma(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration[TiposTramiteEndpoints.ClaveDireccion] is { Length: > 0 } direccion)
        {
            var plazo = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Diagnostico:Usuarios:PlazoRpcSegundos", 10), 1, 60));
            services.AddFlitGrpcClient<UsuariosProductoService.UsuariosProductoServiceClient>(configuration, new Uri(direccion), Scope, o => o.Deadline = plazo);
            services.AddFlitGrpcClient<UsuariosProductoConteoService.UsuariosProductoConteoServiceClient>(configuration, new Uri(direccion), ScopeConteos, o => o.Deadline = plazo);
        }

        services.TryAddScoped<IUsuariosPlataforma>(sp =>
            sp.GetService<UsuariosProductoService.UsuariosProductoServiceClient>() is { } cliente
            && sp.GetService<UsuariosProductoConteoService.UsuariosProductoConteoServiceClient>() is { } conteo
                ? new UsuariosPlataformaGrpc(cliente, conteo)
                : new UsuariosPlataformaNoConectada());
        return services;
    }
}

/// <summary>Adaptador real: arma la metadata de empresa y actor y fija <c>producto = diagnostico</c> en cada pedido.</summary>
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
        var r = await cliente.CambiarRolProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false);
        return r.Usuario;
    }

    public async Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        var pedido = new SuspenderEnProductoRequest { Producto = UsuariosPlataforma.Producto, UsuarioId = usuario.ToString() };
        var r = await cliente.SuspenderEnProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false);
        return r.Usuario;
    }

    public async Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct)
    {
        var pedido = new ReactivarEnProductoRequest { Producto = UsuariosPlataforma.Producto, UsuarioId = usuario.ToString() };
        var r = await cliente.ReactivarEnProductoAsync(pedido, Opciones(contexto.Empresa, contexto.Actor, ct)).ResponseAsync.ConfigureAwait(false);
        return r.Usuario;
    }

    public async Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct)
    {
        // Servicio global: sin empresa objetivo (core-api no la mira en este rpc).
        var r = await conteo.ContarPorEmpresaAsync(new ContarPorEmpresaRequest { Producto = UsuariosPlataforma.Producto }, Opciones(null, actor, ct))
            .ResponseAsync.ConfigureAwait(false);
        return [.. r.Conteos];
    }

    private static CallOptions Opciones(Guid? empresa, Guid actor, CancellationToken ct)
    {
        var cabeceras = new Metadata { { UsuariosPlataforma.ActorMetadata, actor.ToString() } };
        // Explícita: el interceptor del SDK solo pone la del token si falta, y en el backoffice la objetivo es la de la ruta.
        if (empresa is { } id)
            cabeceras.Add(PlatformServiceCallInterceptor.TenantMetadata, id.ToString());
        return new CallOptions(headers: cabeceras, cancellationToken: ct);
    }
}

/// <summary>Sin <c>Diagnostico:Tramites:Address</c>: no hay con quién hablar; 503 <see cref="UsuariosPlataforma.CodigoNoConfigurada"/>.</summary>
internal sealed class UsuariosPlataformaNoConectada : IUsuariosPlataforma
{
    public Task<ListarUsuariosProductoResponse> ListarAsync(ContextoUsuarios contexto, ListarUsuariosProductoRequest pedido, CancellationToken ct) => throw NoConfigurada();

    public Task InvitarAsync(ContextoUsuarios contexto, InvitarUsuarioProductoRequest pedido, CancellationToken ct) => throw NoConfigurada();

    public Task<UsuarioProducto> CambiarRolAsync(ContextoUsuarios contexto, Guid usuario, string rol, CancellationToken ct) => throw NoConfigurada();

    public Task<UsuarioProducto> SuspenderAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct) => throw NoConfigurada();

    public Task<UsuarioProducto> ReactivarAsync(ContextoUsuarios contexto, Guid usuario, CancellationToken ct) => throw NoConfigurada();

    public Task<IReadOnlyList<ConteoEmpresa>> ContarPorEmpresaAsync(Guid actor, CancellationToken ct) => throw NoConfigurada();

    private static ReglaException NoConfigurada() =>
        new(UsuariosPlataforma.CodigoNoConfigurada, "La gestión de usuarios no está disponible: el servicio no tiene configurada la conexión con la plataforma.",
            StatusCodes.Status503ServiceUnavailable);
}
