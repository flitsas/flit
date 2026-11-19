using System.Text.RegularExpressions;
using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Flit.Api.Authorization;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Flit.Api.Grpc;

/// <summary>
/// Adaptador gRPC de <c>flit.platform.v1.ConfiguracionIaService</c> (ADR-0091, contrato §1). Delgado: verifica el cliente y la
/// herramienta (<see cref="DiagnosticoIaServicioAuthorization.ClienteDe"/>), valida <c>tool_code</c>, <c>nonce</c> (16 bytes) y
/// <c>kid</c>, delega en <see cref="IEntregaConfiguracionIa"/> y responde con eco de nonce/kid. Global: no lee empresa ni actor.
/// Errores con <c>ErrorInfo</c> (domain flitsas.com) y mensajes fijos; lo inesperado sale como <c>INTERNAL</c> genérico (SP-19).
/// Auditoría por log estructurado (SP-20): <c>ia.llave.entregada</c> / <c>ia.llave.denegada</c> con sub, jti, tool, kid,
/// row_version, nº de proveedores, motivo e IP; nunca la llave, el sobre ni hash alguno.
/// </summary>
internal sealed partial class ConfiguracionIaGrpcService(IEntregaConfiguracionIa entrega, ILogger<ConfiguracionIaGrpcService> logger)
    : ConfiguracionIaService.ConfiguracionIaServiceBase
{
    internal const string ClienteNoPermitido = "CLIENTE_NO_PERMITIDO";
    internal const string HerramientaNoPermitida = "HERRAMIENTA_NO_PERMITIDA";
    internal const string ToolCodeRequerido = "TOOL_CODE_REQUERIDO";
    internal const string NonceInvalido = "NONCE_INVALIDO";
    internal const string KidInvalido = "KID_INVALIDO";
    internal const string ClaveDestinatarioDesconocida = "CLAVE_DESTINATARIO_DESCONOCIDA";

    private const int BytesNonce = 16;

    public override async Task<ObtenerConfiguracionIaResponse> ObtenerConfiguracionIa(ObtenerConfiguracionIaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var caller = PlatformServiceCaller.From(context);
        var quien = new Quien(caller.ClientId, Jti(context), context.Peer);
        var tool = ParaLog(request.ToolCode);
        var kid = KidValido(request.Kid) ? request.Kid : "(invalido)";

        var cliente = DiagnosticoIaServicioAuthorization.ClienteDe(caller.ClientId)
            ?? throw Denegar(quien, tool, kid, ClienteNoPermitido, StatusCode.PermissionDenied, "Este servicio solo lo atiende Diagnóstico Vehicular.");
        if (string.IsNullOrEmpty(request.ToolCode))
            throw Denegar(quien, tool, kid, ToolCodeRequerido, StatusCode.InvalidArgument, "Falta tool_code.");
        if (!cliente.Herramientas.Contains(request.ToolCode))
            throw Denegar(quien, tool, kid, HerramientaNoPermitida, StatusCode.PermissionDenied, "La herramienta no está permitida para este cliente.");
        if (request.Nonce.Length != BytesNonce)
            throw Denegar(quien, tool, kid, NonceInvalido, StatusCode.InvalidArgument, "El nonce debe tener 16 bytes.");
        if (!KidValido(request.Kid))
            throw Denegar(quien, tool, kid, KidInvalido, StatusCode.InvalidArgument, "kid inválido.");

        try
        {
            if (!entrega.ConoceKid(cliente.Audiencia, request.Kid))
                throw Denegar(quien, tool, kid, ClaveDestinatarioDesconocida, StatusCode.FailedPrecondition, "No hay clave registrada para ese kid.");

            var r = await entrega.EntregarAsync(new SolicitudEntregaIa(cliente.Audiencia, request.ToolCode, request.Kid, request.Nonce.ToByteArray()), context.CancellationToken)
                .ConfigureAwait(false);
            LogEntregada(logger, quien.Sub, quien.Jti, tool, kid, r.RowVersion, r.Cola.Count, r.Motivo, quien.Ip);
            return ToProto(r, request);
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
            // SP-19: solo el tipo; ni mensaje, ni stack, ni configuración.
            LogFalla(logger, quien.Sub, tool, ex.GetType().Name, quien.Ip);
            throw new RpcException(new Status(StatusCode.Internal, "Error interno al entregar la configuración de IA."));
        }
    }

    private static ObtenerConfiguracionIaResponse ToProto(EntregaConfiguracionIa r, ObtenerConfiguracionIaRequest request)
    {
        var response = new ObtenerConfiguracionIaResponse
        {
            ToolCode = r.ToolCode,
            Disponible = r.Disponible,
            Motivo = r.Motivo switch
            {
                MotivoNoDisponibleIa.HerramientaSinConfigurar => MotivoNoDisponible.HerramientaSinConfigurar,
                MotivoNoDisponibleIa.SinProveedorConLlave => MotivoNoDisponible.SinProveedorConLlave,
                _ => MotivoNoDisponible.Unspecified,
            },
            ProveedorActivo = r.ProveedorActivo,
            RetornoAutomatico = r.RetornoAutomatico,
            RowVersion = r.RowVersion,
            TtlSegundos = r.TtlSegundos,
            EmitidaEn = new Timestamp { Seconds = r.EmitidaEn.ToUnixTimeSeconds(), Nanos = 0 },
            Nonce = request.Nonce,
            Kid = request.Kid,
        };
        response.Cola.AddRange(r.Cola.Select(p => new ProveedorIa
        {
            Proveedor = p.Proveedor,
            Prioridad = p.Prioridad,
            Modelo = p.Modelo,
            Llave = new LlaveCifrada
            {
                Alg = p.Llave.Alg,
                Kid = p.Llave.Kid,
                CekCifrada = ByteString.CopyFrom(p.Llave.CekCifrada),
                Iv = ByteString.CopyFrom(p.Llave.Iv),
                TextoCifrado = ByteString.CopyFrom(p.Llave.TextoCifrado),
                Tag = ByteString.CopyFrom(p.Llave.Tag),
            },
        }));
        return response;
    }

    private RpcException Denegar(Quien quien, string tool, string kid, string reason, StatusCode estado, string mensaje)
    {
        LogDenegada(logger, quien.Sub, quien.Jti, tool, kid, reason, quien.Ip);
        return PlatformRpcErrors.ToRpcException(reason, mensaje, estado);
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

    /// <summary>El tool del request solo entra al log si tiene forma de código (sin saltos ni texto libre).</summary>
    private static string ParaLog(string? valor) =>
        valor is { Length: > 0 and <= 64 } && valor.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') ? valor : "(invalido)";

    private static bool KidValido(string? kid) => kid is not null && KidRegex().IsMatch(kid);

    // Misma forma que valida el AAD (contrato §1: 1..64 [a-z0-9-]); \z para no aceptar un salto de línea final.
    [GeneratedRegex(@"^[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex KidRegex();

    private sealed record Quien(string Sub, string? Jti, string Ip);

    [LoggerMessage(EventId = 7310, EventName = "ia.llave.entregada", Level = LogLevel.Information,
        Message = "ia.llave.entregada sub={Sub} jti={Jti} tool={Tool} kid={Kid} rowVersion={RowVersion} proveedores={Proveedores} motivo={Motivo} ip={Ip}")]
    private static partial void LogEntregada(ILogger logger, string sub, string? jti, string tool, string kid, long rowVersion, int proveedores, MotivoNoDisponibleIa motivo, string ip);

    [LoggerMessage(EventId = 7311, EventName = "ia.llave.denegada", Level = LogLevel.Warning,
        Message = "ia.llave.denegada sub={Sub} jti={Jti} tool={Tool} kid={Kid} reason={Reason} ip={Ip}")]
    private static partial void LogDenegada(ILogger logger, string sub, string? jti, string tool, string kid, string reason, string ip);

    [LoggerMessage(EventId = 7312, EventName = "ia.llave.error", Level = LogLevel.Error,
        Message = "ia.llave.error sub={Sub} tool={Tool} tipo={Tipo} ip={Ip}")]
    private static partial void LogFalla(ILogger logger, string sub, string tool, string tipo, string ip);
}
