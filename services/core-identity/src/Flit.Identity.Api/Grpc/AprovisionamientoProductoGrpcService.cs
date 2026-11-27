using Flit.Api.Identity;
using Flit.Identidad.Grpc.V1;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1; contracts/proto/flit/identidad/v1/aprovisionamiento_producto.proto) —
/// <c>AprovisionamientoProductoService.ReportarEstadoAprovisionamiento</c>: el servicio de un producto con aprovisionamiento
/// (hoy solo <c>svc-impuestos</c>) reporta su estado por empresa y core-identity lo guarda en
/// <c>platform.tenant_product_provisioning</c> para el admin de compañías. El producto sale del token
/// (<c>svc-&lt;código&gt;</c>), nunca del mensaje. Idempotente por versión.
/// </summary>
internal sealed partial class AprovisionamientoProductoGrpcService(
    ReportProvisioningStatusHandler handler,
    ILogger<AprovisionamientoProductoGrpcService> logger)
    : AprovisionamientoProductoService.AprovisionamientoProductoServiceBase
{
    public const string Scope = "platform.aprovisionamiento.reportar";
    private const string ClienteNoPermitido = "CLIENTE_NO_PERMITIDO";

    public override async Task<ReportarEstadoAprovisionamientoResponse> ReportarEstadoAprovisionamiento(
        ReportarEstadoAprovisionamientoRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var caller = PlatformServiceCaller.From(context);
        if (caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Falta x-flit-tenant-id."));

        var producto = ProductoDelCliente(caller.ClientId);
        if (producto is null)
        {
            LogDenegado(logger, caller.ClientId, caller.TenantId);
            throw PlatformRpcErrors.ToRpcException(
                ClienteNoPermitido,
                "Este servicio solo lo atienden los productos con aprovisionamiento.",
                StatusCode.PermissionDenied);
        }

        try
        {
            var guardado = await handler.HandleAsync(
                    new ReportProvisioningStatusCommand(
                        caller.TenantId,
                        producto,
                        request.Estado,
                        request.MotivoCodigo,
                        request.Mensaje,
                        request.Accion,
                        request.FirmaConfirmada,
                        request.Version),
                    context.CancellationToken)
                .ConfigureAwait(false);

            LogReportado(logger, producto, caller.TenantId, request.Estado, request.Version, guardado.Aplicado, guardado.VersionGuardada);
            return new ReportarEstadoAprovisionamientoResponse { Aplicado = guardado.Aplicado, VersionGuardada = guardado.VersionGuardada };
        }
        catch (TenantProductException ex)
        {
            var estado = ex.Code switch
            {
                TenantProductException.TenantNotFound => StatusCode.NotFound,
                TenantProductException.ProductWithoutProvisioning => StatusCode.FailedPrecondition,
                _ => StatusCode.InvalidArgument,
            };
            throw PlatformRpcErrors.ToRpcException(ex.Code, ex.Message, estado);
        }
    }

    /// <summary><c>svc-&lt;código&gt;</c> → código, solo si el producto tiene aprovisionamiento.</summary>
    internal static string? ProductoDelCliente(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId) || !clientId.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal))
            return null;
        var codigo = clientId[OidcDefaults.ServiceClientPrefix.Length..];
        return ProductosConAprovisionamiento.Codigos.Contains(codigo) ? codigo : null;
    }

    [LoggerMessage(EventId = 7420, EventName = "aprovisionamiento.estado.reportado", Level = LogLevel.Information,
        Message = "aprovisionamiento.estado.reportado producto={Producto} tenant={Tenant} estado={Estado} version={Version} aplicado={Aplicado} guardada={Guardada}")]
    private static partial void LogReportado(ILogger logger, string producto, Guid tenant, string estado, long version, bool aplicado, long guardada);

    [LoggerMessage(EventId = 7421, EventName = "aprovisionamiento.estado.denegado", Level = LogLevel.Warning,
        Message = "aprovisionamiento.estado.denegado sub={Sub} tenant={Tenant}")]
    private static partial void LogDenegado(ILogger logger, string sub, Guid tenant);
}
