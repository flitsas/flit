using System.Net.Sockets;
using Flit.Api.Grpc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Flit.Api.Authorization;

/// <summary>
/// Auditoría del rechazo por policy de <c>ConfiguracionIaService</c> (ADR-0091, SP-20; review HU-6 CR-2). La policy
/// <see cref="DiagnosticoIaServicioAuthorization.Policy"/> corta la llamada antes de llegar al servicio gRPC, así que un token de
/// otro <c>svc-*</c> o de <c>svc-diagnostico</c> sin el scope no pasaba por <c>ia.llave.denegada</c>. Cuando falla ESA policy se
/// audita con el mismo evento del servicio (sub, jti, IP y motivo <see cref="PolicyRechazada"/>; nunca token, llave ni sobre) y
/// se delega en <see cref="AuthorizationMiddlewareResultHandler"/> por defecto. Para cualquier otra policy solo delega, en el
/// handler que ya estaba registrado (<see cref="SuperAdminForbiddenResultHandler"/>), para no cambiar las respuestas 403 del resto.
/// </summary>
internal sealed class DiagnosticoIaAuthorizationResultHandler(ILogger<ConfiguracionIaGrpcService> logger) : IAuthorizationMiddlewareResultHandler
{
    internal const string PolicyRechazada = "POLICY_RECHAZADA";

    /// <summary>El cuerpo gRPC no se lee en la autorización: tool y kid no se conocen.</summary>
    private const string NoLeido = "(no_leido)";

    private readonly AuthorizationMiddlewareResultHandler _porDefecto = new();
    private readonly SuperAdminForbiddenResultHandler _resto = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Succeeded || !EsLaPolicyDeIa(context))
            return _resto.HandleAsync(next, context, policy, authorizeResult);

        ConfiguracionIaGrpcService.LogDenegada(logger, ParaLog(Claim(context, "sub")) ?? "(ninguno)", ParaLog(Claim(context, "jti")),
            NoLeido, NoLeido, PolicyRechazada, Peer(context));
        return _porDefecto.HandleAsync(next, context, policy, authorizeResult);
    }

    private static bool EsLaPolicyDeIa(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => string.Equals(a.Policy, DiagnosticoIaServicioAuthorization.Policy, StringComparison.Ordinal)) == true;

    private static string? Claim(HttpContext context, string tipo) =>
        context.User.Identities.Select(i => i.FindFirst(tipo)?.Value).FirstOrDefault(v => v is not null);

    /// <summary>Solo valores con forma de identificador (sin saltos ni texto libre) entran al log.</summary>
    private static string? ParaLog(string? valor) =>
        valor is null ? null
        : valor.Length is > 0 and <= 128 && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '@') ? valor
        : "(invalido)";

    /// <summary>Mismo formato que <c>ServerCallContext.Peer</c> de Grpc.AspNetCore (<c>ipv4:ip:puerto</c>).</summary>
    private static string Peer(HttpContext context)
    {
        var conexion = context.Connection;
        if (conexion.RemoteIpAddress is not { } ip)
            return "unknown";
        return ip.AddressFamily switch
        {
            AddressFamily.InterNetwork => $"ipv4:{ip}:{conexion.RemotePort}",
            AddressFamily.InterNetworkV6 => $"ipv6:[{ip}]:{conexion.RemotePort}",
            _ => $"unknown:{ip}:{conexion.RemotePort}",
        };
    }
}
