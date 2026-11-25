using Flit.Identidad.Grpc.V1;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// Entrega la API key del impuesto a <c>svc-consultas</c> (ADR-0092). Fail-closed con Diagnóstico
/// apagado; nunca escribe la llave en logs.
/// </summary>
internal sealed partial class CredencialesProductoGrpcService(
    GetTenantProductCredentialHandler handler,
    ILogger<CredencialesProductoGrpcService> logger)
    : CredencialesProductoService.CredencialesProductoServiceBase
{
    public const string Scope = "platform.identidad.credenciales.impuesto";
    private const string ClientePermitido = "svc-consultas";
    private const string ClienteNoPermitido = "CLIENTE_NO_PERMITIDO";
    private const int TtlConLlave = 300;
    private const int TtlSinLlave = 60;

    public override async Task<ObtenerCredencialImpuestoResponse> ObtenerCredencialImpuesto(
        ObtenerCredencialImpuestoRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var caller = PlatformServiceCaller.From(context);
        var quien = new Quien(caller.ClientId, Jti(context), caller.TenantId, context.Peer);

        if (caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Falta x-flit-tenant-id."));

        if (!string.Equals(caller.ClientId, ClientePermitido, StringComparison.Ordinal))
        {
            LogDenegada(logger, quien.Sub, quien.Jti, quien.Tenant, 0, ClienteNoPermitido, quien.Ip);
            throw PlatformRpcErrors.ToRpcException(
                ClienteNoPermitido,
                "Este servicio solo lo atiende Consultas.",
                StatusCode.PermissionDenied);
        }

        try
        {
            var entrega = await handler.HandleAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false);
            if (!entrega.Disponible)
            {
                var motivo = entrega.Motivo switch
                {
                    MotivoCredencialImpuesto.ProductoApagado => MotivoCredencialNoDisponible.ProductoApagado,
                    _ => MotivoCredencialNoDisponible.SinConfigurar,
                };
                LogDenegada(logger, quien.Sub, quien.Jti, quien.Tenant, entrega.Version, motivo.ToString(), quien.Ip);
                return new ObtenerCredencialImpuestoResponse
                {
                    Disponible = false,
                    Motivo = motivo,
                    ApiKey = string.Empty,
                    Version = entrega.Version,
                    TtlSegundos = TtlSinLlave,
                };
            }

            LogEntregada(logger, quien.Sub, quien.Jti, quien.Tenant, entrega.Version, quien.Ip);
            return new ObtenerCredencialImpuestoResponse
            {
                Disponible = true,
                Motivo = MotivoCredencialNoDisponible.Unspecified,
                ApiKey = entrega.Value ?? string.Empty,
                Version = entrega.Version,
                TtlSegundos = TtlConLlave,
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFalla(logger, quien.Sub, quien.Tenant, ex.GetType().Name, quien.Ip);
            throw new RpcException(new Status(StatusCode.Internal, "Error interno al entregar la credencial."));
        }
    }

    private static string? Jti(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext().User.FindFirst("jti")?.Value;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record Quien(string Sub, string? Jti, Guid Tenant, string Ip);

    [LoggerMessage(EventId = 7410, EventName = "impuesto.credencial.entregada", Level = LogLevel.Information,
        Message = "impuesto.credencial.entregada sub={Sub} jti={Jti} tenant={Tenant} version={Version} ip={Ip}")]
    private static partial void LogEntregada(ILogger logger, string sub, string? jti, Guid tenant, long version, string ip);

    [LoggerMessage(EventId = 7411, EventName = "impuesto.credencial.denegada", Level = LogLevel.Warning,
        Message = "impuesto.credencial.denegada sub={Sub} jti={Jti} tenant={Tenant} version={Version} motivo={Motivo} ip={Ip}")]
    private static partial void LogDenegada(ILogger logger, string sub, string? jti, Guid tenant, long version, string motivo, string ip);

    [LoggerMessage(EventId = 7412, EventName = "impuesto.credencial.error", Level = LogLevel.Error,
        Message = "impuesto.credencial.error sub={Sub} tenant={Tenant} tipo={Tipo} ip={Ip}")]
    private static partial void LogFalla(ILogger logger, string sub, Guid tenant, string tipo, string ip);
}
