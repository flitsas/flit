using Flit.Api.Authorization;
using Flit.Api.Grpc;
using Flit.Api.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Api.Usuarios;

/// <summary>
/// Composición de los rpc de usuarios por producto (ADR-0089, HU #14587): <c>UsuariosProductoService</c> (por empresa,
/// scope <c>platform.usuarios.diagnostico</c>) y <c>UsuariosProductoConteoService</c> (global, scope <c>.conteos</c>). El
/// interceptor del SDK exige token de servicio con <c>aud=tramites</c> y el scope; la policy, el cliente
/// <c>svc-diagnostico</c>. Siempre mapeados: sin el scope en el cliente de Identidad nadie entra.
/// </summary>
internal static class UsuariosProductoExtensions
{
    public static IServiceCollection AddUsuariosProducto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProductRoleRepository, ProductRoleRepository>();
        services.AddScoped<IActorUsuariosProducto, ActorUsuariosProducto>();
        services.AddScoped<IProductInvitationSender, InvitacionProductoPorPlataforma>();
        services.AddScoped<ListProductUsersHandler>();
        services.AddScoped<InviteProductUserHandler>();
        services.AddScoped<AssignProductRoleHandler>();
        services.AddScoped<SuspendProductRoleHandler>();
        services.AddScoped<ReactivateProductRoleHandler>();

        services.AddGrpc()
            .RequireServiceToken<UsuariosProductoGrpcService>(DiagnosticoUsuariosServicioAuthorization.Scope, ServiceAudiences.Tramites)
            .RequireServiceTokenSinEmpresa<UsuariosProductoConteoGrpcService>(DiagnosticoUsuariosServicioAuthorization.ScopeConteos, ServiceAudiences.Tramites);
        return services;
    }

    public static IEndpointRouteBuilder MapUsuariosProducto(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGrpcService<UsuariosProductoGrpcService>().RequireAuthorization(DiagnosticoUsuariosServicioAuthorization.Policy);
        app.MapGrpcService<UsuariosProductoConteoGrpcService>().RequireAuthorization(DiagnosticoUsuariosServicioAuthorization.PolicyConteos);
        return app;
    }
}

/// <summary>
/// Invitación nueva desde un producto sobre <see cref="CreateInvitationHandler"/> (correo, tema de la red y enlace de
/// activación de siempre) con UN rol. Si otra invitación o cuenta con ese correo se adelantó, no falla: devuelve
/// <c>null</c> y el caso de uso lo trata como «invitación pendiente» (respuesta uniforme, SP-15).
/// </summary>
internal sealed class InvitacionProductoPorPlataforma(CreateInvitationHandler handler) : IProductInvitationSender
{
    public async Task<ProductInvitationSent?> SendAsync(Guid tenantId, string email, string fullName, Guid roleId, Guid invitedBy, CancellationToken ct)
    {
        try
        {
            var created = await handler.HandleAsync(new CreateInvitationCommand(tenantId, email, fullName, [roleId], invitedBy), ct).ConfigureAwait(false);
            return new ProductInvitationSent(created.InvitationId, created.EmailSent);
        }
        catch (Exception ex) when (ex is InvitationAlreadyPendingException or UserAlreadyExistsException)
        {
            return null;
        }
    }
}
