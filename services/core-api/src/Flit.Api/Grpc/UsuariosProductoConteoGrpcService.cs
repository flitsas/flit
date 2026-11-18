using Flit.Api.Authorization;
using Flit.Api.Usuarios;
using Flit.Modules.Security.Application.UserRoles.ProductRoles;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Adaptador gRPC global de <c>flit.platform.v1.UsuariosProductoConteoService</c> (ADR-0089): conteo de usuarios del producto
/// por empresa para la tabla de compañías del SuperAdmin. Sin empresa en la metadata; el actor tiene que ser SuperAdmin
/// reverificado en la base. Solo devuelve ids de empresa y números (sin PII).
/// </summary>
internal sealed class UsuariosProductoConteoGrpcService(IActorUsuariosProducto actores, ListProductUsersHandler listar)
    : UsuariosProductoConteoService.UsuariosProductoConteoServiceBase
{
    public override async Task<ContarPorEmpresaResponse> ContarPorEmpresa(ContarPorEmpresaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var actorId = DiagnosticoUsuariosServicioAuthorization.VerificarLlamante(caller, request.Producto, context.RequestHeaders, exigeEmpresa: false);
        var actor = await actores.VerificarAsync(null, actorId, request.Producto, context.CancellationToken).ConfigureAwait(false);
        if (actor is not { IsSuperAdmin: true })
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Solo el SuperAdmin consulta los conteos de todas las empresas."));

        var conteos = await listar.CountByTenantAsync(request.Producto, context.CancellationToken).ConfigureAwait(false);
        var response = new ContarPorEmpresaResponse();
        response.Conteos.AddRange(conteos.Select(c => new ConteoEmpresa { EmpresaId = c.TenantId.ToString(), Usuarios = c.Users, Administradores = c.Admins }));
        return response;
    }
}
