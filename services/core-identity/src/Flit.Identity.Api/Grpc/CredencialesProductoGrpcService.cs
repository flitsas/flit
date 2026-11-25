using Flit.Identidad.Grpc.V1;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Identity.Api.Grpc;

/// <summary>
/// Entrega la API key del impuesto a <c>svc-consultas</c> (ADR-0092). Fail-closed con Diagnóstico
/// apagado; nunca escribe la llave en logs. HU #14694: <see cref="ObtenerCredencialImpuestoParaCopia"/> la entrega a
/// <c>svc-impuestos</c> (solo ese rpc, con su scope <see cref="ScopeCopia"/>) aunque Diagnóstico esté apagado, auditada; y
/// <see cref="ActualizarCredencialImpuestoDiagnostico"/> (M04b, scope <see cref="ScopeActualizar"/>) reemplaza esa llave cuando el
/// alta del proveedor la rotó (D-ALTA-IDEM). HU #14711: <see cref="ObtenerSecretoWebhookImpuesto"/> entrega a <c>svc-consultas</c>
/// (scope <see cref="ScopeWebhook"/>) el secreto del webhook del impuesto para verificar los avisos del proveedor.
/// </summary>
internal sealed partial class CredencialesProductoGrpcService(
    GetTenantProductCredentialHandler handler,
    GetTenantProductCredentialForCopyHandler copia,
    UpdateTenantProductCredentialFromServiceHandler actualizar,
    GetTenantProductWebhookSecretHandler secretoWebhook,
    ILogger<CredencialesProductoGrpcService> logger)
    : CredencialesProductoService.CredencialesProductoServiceBase
{
    public const string Scope = "platform.identidad.credenciales.impuesto";

    /// <summary>HU #14694 — scope del rpc de copia (solo <c>svc-impuestos</c>).</summary>
    public const string ScopeCopia = "platform.identidad.credenciales.impuesto.copia";
    /// <summary>HU #14694 (M04b) — scope del rpc que reemplaza la llave de Diagnóstico (solo <c>svc-impuestos</c>).</summary>
    public const string ScopeActualizar = "platform.identidad.credenciales.impuesto.actualizar";
    /// <summary>HU #14711 — scope del rpc del secreto del webhook del impuesto (solo <c>svc-consultas</c>).</summary>
    public const string ScopeWebhook = "platform.identidad.credenciales.impuesto.webhook";
    private const string ClientePermitido = "svc-consultas";
    private const string ClienteCopia = "svc-impuestos";
    private const string ClienteNoPermitido = "CLIENTE_NO_PERMITIDO";
    private const string CredencialInvalida = "CREDENCIAL_INVALIDA";
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

    /// <summary>
    /// HU #14694 (M04b, D-ALTA-IDEM) — el alta del proveedor rotó la llave de la empresa y core-impuestos propaga la nueva a
    /// Diagnóstico. Solo <c>svc-impuestos</c> (scope <see cref="ScopeActualizar"/>); solo reemplaza una fila que ya existe
    /// (<c>actualizada=false</c> sin ella, nunca la crea). Llave con forma inválida: <c>INVALID_ARGUMENT</c>
    /// <c>CREDENCIAL_INVALIDA</c>. Auditado en el repositorio con el cliente; la llave nunca entra a un log ni a un error.
    /// </summary>
    public override async Task<ActualizarCredencialImpuestoDiagnosticoResponse> ActualizarCredencialImpuestoDiagnostico(
        ActualizarCredencialImpuestoDiagnosticoRequest request,
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
            LogActualizacionDenegada(logger, quien.Sub, quien.Jti, quien.Tenant, ClienteNoPermitido, quien.Ip);
            throw PlatformRpcErrors.ToRpcException(
                ClienteNoPermitido,
                "Este rpc solo lo atiende Impuestos.",
                StatusCode.PermissionDenied);
        }

        try
        {
            var actualizada = await actualizar.HandleAsync(caller.TenantId, request.ApiKey, caller.ClientId, context.CancellationToken)
                .ConfigureAwait(false);
            if (actualizada)
                LogActualizada(logger, quien.Sub, quien.Jti, quien.Tenant, quien.Ip);
            else
                LogActualizacionSinFila(logger, quien.Sub, quien.Jti, quien.Tenant, quien.Ip);
            return new ActualizarCredencialImpuestoDiagnosticoResponse { Actualizada = actualizada };
        }
        catch (TenantProductException ex) when (ex.Code == TenantProductException.CredentialInvalid)
        {
            LogActualizacionDenegada(logger, quien.Sub, quien.Jti, quien.Tenant, CredencialInvalida, quien.Ip);
            throw PlatformRpcErrors.ToRpcException(CredencialInvalida, "La llave no cumple el formato permitido.", StatusCode.InvalidArgument);
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
            throw new RpcException(new Status(StatusCode.Internal, "Error interno al actualizar la credencial."));
        }
    }

    /// <summary>
    /// HU #14711 (plan §3.2) — el secreto del webhook del impuesto para que core-consultas verifique los avisos del proveedor:
    /// el actual, el anterior mientras siga vigente y la versión (<c>row_version</c> del actual). Solo <c>svc-consultas</c>
    /// (scope <see cref="ScopeWebhook"/>); fail-closed con Diagnóstico apagado. Los secretos nunca entran a un log ni a un error.
    /// </summary>
    public override async Task<ObtenerSecretoWebhookImpuestoResponse> ObtenerSecretoWebhookImpuesto(
        ObtenerSecretoWebhookImpuestoRequest request,
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
            LogSecretoWebhookDenegado(logger, quien.Sub, quien.Jti, quien.Tenant, ClienteNoPermitido, quien.Ip);
            throw PlatformRpcErrors.ToRpcException(
                ClienteNoPermitido,
                "Este rpc solo lo atiende Consultas.",
                StatusCode.PermissionDenied);
        }

        try
        {
            var entrega = await secretoWebhook.HandleAsync(caller.TenantId, context.CancellationToken).ConfigureAwait(false);
            if (!entrega.Configurado)
            {
                // M-4: una empresa sin secreto todavía no es una denegación (Information 7408); solo el producto apagado lo es (7409).
                if (entrega.Motivo == MotivoCredencialImpuesto.SinConfigurar)
                    LogSecretoWebhookSinConfigurar(logger, quien.Sub, quien.Jti, quien.Tenant, quien.Ip);
                else
                    LogSecretoWebhookDenegado(logger, quien.Sub, quien.Jti, quien.Tenant, entrega.Motivo.ToString(), quien.Ip);
                return new ObtenerSecretoWebhookImpuestoResponse { Configurado = false, Actual = string.Empty, Anterior = string.Empty, Version = 0 };
            }

            LogSecretoWebhookEntregado(logger, quien.Sub, quien.Jti, quien.Tenant, entrega.Version, entrega.Anterior is not null, quien.Ip);
            var respuesta = new ObtenerSecretoWebhookImpuestoResponse
            {
                Configurado = true,
                Actual = entrega.Actual ?? string.Empty,
                Anterior = entrega.Anterior ?? string.Empty,
                Version = entrega.Version,
            };
            if (entrega.Anterior is not null && entrega.AnteriorVenceEn is { } vence)
                respuesta.AnteriorVenceEn = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(vence);
            return respuesta;
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
            throw new RpcException(new Status(StatusCode.Internal, "Error interno al entregar el secreto del webhook."));
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

    [LoggerMessage(EventId = 7416, EventName = "impuesto.credencial.diagnostico.actualizada", Level = LogLevel.Information,
        Message = "impuesto.credencial.diagnostico.actualizada sub={Sub} jti={Jti} tenant={Tenant} ip={Ip}")]
    private static partial void LogActualizada(ILogger logger, string sub, string? jti, Guid tenant, string ip);

    [LoggerMessage(EventId = 7417, EventName = "impuesto.credencial.diagnostico.sin_fila", Level = LogLevel.Information,
        Message = "impuesto.credencial.diagnostico.sin_fila sub={Sub} jti={Jti} tenant={Tenant} ip={Ip}")]
    private static partial void LogActualizacionSinFila(ILogger logger, string sub, string? jti, Guid tenant, string ip);

    [LoggerMessage(EventId = 7418, EventName = "impuesto.credencial.diagnostico.denegada", Level = LogLevel.Warning,
        Message = "impuesto.credencial.diagnostico.denegada sub={Sub} jti={Jti} tenant={Tenant} motivo={Motivo} ip={Ip}")]
    private static partial void LogActualizacionDenegada(ILogger logger, string sub, string? jti, Guid tenant, string motivo, string ip);

    [LoggerMessage(EventId = 7419, EventName = "impuesto.webhook.secreto.entregado", Level = LogLevel.Information,
        Message = "impuesto.webhook.secreto.entregado sub={Sub} jti={Jti} tenant={Tenant} version={Version} con_anterior={ConAnterior} ip={Ip}")]
    private static partial void LogSecretoWebhookEntregado(ILogger logger, string sub, string? jti, Guid tenant, long version, bool conAnterior, string ip);

    [LoggerMessage(EventId = 7408, EventName = "impuesto.webhook.secreto.sin_configurar", Level = LogLevel.Information,
        Message = "impuesto.webhook.secreto.sin_configurar sub={Sub} jti={Jti} tenant={Tenant} ip={Ip}")]
    private static partial void LogSecretoWebhookSinConfigurar(ILogger logger, string sub, string? jti, Guid tenant, string ip);

    [LoggerMessage(EventId = 7409, EventName = "impuesto.webhook.secreto.denegado", Level = LogLevel.Warning,
        Message = "impuesto.webhook.secreto.denegado sub={Sub} jti={Jti} tenant={Tenant} motivo={Motivo} ip={Ip}")]
    private static partial void LogSecretoWebhookDenegado(ILogger logger, string sub, string? jti, Guid tenant, string motivo, string ip);

    [LoggerMessage(EventId = 7412, EventName = "impuesto.credencial.error", Level = LogLevel.Error,
        Message = "impuesto.credencial.error sub={Sub} tenant={Tenant} tipo={Tipo} ip={Ip}")]
    private static partial void LogFalla(ILogger logger, string sub, Guid tenant, string tipo, string ip);
}
