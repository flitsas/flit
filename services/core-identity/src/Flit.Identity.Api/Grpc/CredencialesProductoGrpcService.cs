using Flit.Identidad.Grpc.V1;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// Entrega la API key del impuesto a <c>svc-consultas</c> (ADR-0092). Fail-closed con Diagnóstico
/// apagado; nunca escribe la llave en logs. HU #14694: <see cref="ObtenerCredencialImpuestoParaCopia"/> la entrega a
/// <c>svc-impuestos</c> (solo ese rpc, con su scope <see cref="ScopeCopia"/>) aunque Diagnóstico esté apagado, auditada.
/// </summary>
internal sealed partial class CredencialesProductoGrpcService(
    GetTenantProductCredentialHandler handler,
    GetTenantProductCredentialForCopyHandler copia,
    ILogger<CredencialesProductoGrpcService> logger)
    : CredencialesProductoService.CredencialesProductoServiceBase
{
    public const string Scope = "platform.identidad.credenciales.impuesto";

    /// <summary>HU #14694 — scope del rpc de copia (solo <c>svc-impuestos</c>).</summary>
    public const string ScopeCopia = "platform.identidad.credenciales.impuesto.copia";
    private const string ClientePermitido = "svc-consultas";
    private const string ClienteCopia = "svc-impuestos";
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

    /// <summary>
    /// HU #14694 (D-CRED-DIAG, DA-3 residual) — la llave <c>diagnostico/impuesto_api_key</c> para que core-impuestos la copie
    /// a su almacén al aprovisionar. Solo <c>svc-impuestos</c>; cada llamada queda auditada (con o sin fila).
    /// </summary>
    public override async Task<ObtenerCredencialImpuestoParaCopiaResponse> ObtenerCredencialImpuestoParaCopia(
        ObtenerCredencialImpuestoParaCopiaRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var caller = PlatformServiceCaller.From(context);
        var quien = new Quien(caller.ClientId, Jti(context), caller.TenantId, context.Peer);

        if (caller.TenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Falta x-flit-tenant-id."));

        if (!string.Equals(caller.ClientId, ClienteCopia, StringComparison.Ordinal))
        {
            LogCopiaDenegada(logger, quien.Sub, quien.Jti, quien.Tenant, ClienteNoPermitido, quien.Ip);
            throw PlatformRpcErrors.ToRpcException(
                ClienteNoPermitido,
                "Este rpc solo lo atiende Impuestos.",
                StatusCode.PermissionDenied);
        }

        try
        {
            var entrega = await copia.HandleAsync(caller.TenantId, caller.ClientId, context.CancellationToken).ConfigureAwait(false);
            if (!entrega.Disponible)
            {
                LogCopiaSinLlave(logger, quien.Sub, quien.Jti, quien.Tenant, quien.Ip);
                return new ObtenerCredencialImpuestoParaCopiaResponse { Existe = false, ApiKey = string.Empty, Version = 0 };
            }

            LogCopiaEntregada(logger, quien.Sub, quien.Jti, quien.Tenant, entrega.Version, quien.Ip);
            return new ObtenerCredencialImpuestoParaCopiaResponse { Existe = true, ApiKey = entrega.Value ?? string.Empty, Version = entrega.Version };
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

    private sealed record Quien(string Sub, string? Jti, Guid Tenant, string Ip);

    [LoggerMessage(EventId = 7410, EventName = "impuesto.credencial.entregada", Level = LogLevel.Information,
        Message = "impuesto.credencial.entregada sub={Sub} jti={Jti} tenant={Tenant} version={Version} ip={Ip}")]
    private static partial void LogEntregada(ILogger logger, string sub, string? jti, Guid tenant, long version, string ip);

    [LoggerMessage(EventId = 7411, EventName = "impuesto.credencial.denegada", Level = LogLevel.Warning,
        Message = "impuesto.credencial.denegada sub={Sub} jti={Jti} tenant={Tenant} version={Version} motivo={Motivo} ip={Ip}")]
    private static partial void LogDenegada(ILogger logger, string sub, string? jti, Guid tenant, long version, string motivo, string ip);

    [LoggerMessage(EventId = 7413, EventName = "impuesto.credencial.copia.entregada", Level = LogLevel.Information,
        Message = "impuesto.credencial.copia.entregada sub={Sub} jti={Jti} tenant={Tenant} version={Version} ip={Ip}")]
    private static partial void LogCopiaEntregada(ILogger logger, string sub, string? jti, Guid tenant, long version, string ip);

    [LoggerMessage(EventId = 7414, EventName = "impuesto.credencial.copia.sin_llave", Level = LogLevel.Information,
        Message = "impuesto.credencial.copia.sin_llave sub={Sub} jti={Jti} tenant={Tenant} ip={Ip}")]
    private static partial void LogCopiaSinLlave(ILogger logger, string sub, string? jti, Guid tenant, string ip);

    [LoggerMessage(EventId = 7415, EventName = "impuesto.credencial.copia.denegada", Level = LogLevel.Warning,
        Message = "impuesto.credencial.copia.denegada sub={Sub} jti={Jti} tenant={Tenant} motivo={Motivo} ip={Ip}")]
    private static partial void LogCopiaDenegada(ILogger logger, string sub, string? jti, Guid tenant, string motivo, string ip);

    [LoggerMessage(EventId = 7412, EventName = "impuesto.credencial.error", Level = LogLevel.Error,
        Message = "impuesto.credencial.error sub={Sub} tenant={Tenant} tipo={Tipo} ip={Ip}")]
    private static partial void LogFalla(ILogger logger, string sub, Guid tenant, string tipo, string ip);
}
