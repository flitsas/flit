using Flit.Platform.Sdk.Authentication;
using Flit.Platform.Sdk.Tenancy;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>Quién llama y por qué empresa, ya validado por <see cref="PlatformServiceCallInterceptor"/>.</summary>
public sealed record PlatformServiceCaller(string ClientId, Guid TenantId)
{
    private const string Key = "flit.serviceCaller";

    public static PlatformServiceCaller From(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.UserState.TryGetValue(Key, out var value) && value is PlatformServiceCaller caller
            ? caller
            : throw new RpcException(new Status(StatusCode.Internal, "La llamada no pasó por la validación del servicio."));
    }

    internal void Store(ServerCallContext context) => context.UserState[Key] = this;
}

/// <summary>
/// Valida una llamada gRPC entre servicios (contrato de plataforma v1.3 §3 y §6.1, ADR-0070). La firma, el emisor y la
/// vigencia ya los validó la autenticación de ASP.NET Core; aquí, quién es y por quién actúa:
/// <list type="bullet">
///   <item>Sin token, o con token de usuario: <c>UNAUTHENTICATED</c>. Un token de usuario nunca puede elegir empresa.</item>
///   <item>Token de servicio sin la audiencia de este servicio o sin el scope: <c>PERMISSION_DENIED</c>.</item>
///   <item>Sin <c>x-flit-tenant-id</c>, o que no es un UUID: <c>INVALID_ARGUMENT</c>.</item>
/// </list>
/// </summary>
public sealed class PlatformServiceCallInterceptor(string requiredScope, string requiredAudience) : Interceptor
{
    public const string TenantMetadata = "x-flit-tenant-id";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Validate(context.GetHttpContext().User, context.RequestHeaders.GetValue(TenantMetadata)).Store(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public PlatformServiceCaller Validate(System.Security.Claims.ClaimsPrincipal user, string? tenantHeader)
    {
        ArgumentNullException.ThrowIfNull(user);
        var subject = user.FindFirst("sub")?.Value;
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(subject))
            throw Fail(StatusCode.Unauthenticated, "Falta un token de servicio válido.");
        if (!PlatformPrincipal.IsServiceSubject(subject))
            throw Fail(StatusCode.Unauthenticated, "Esta llamada solo acepta tokens de servicio.");
        if (!user.FindAll("aud").Any(c => c.Value == requiredAudience))
            throw PlatformRpcErrors.ToRpcException(PlatformErrorCodes.TokenAudienceMismatch, $"El token no es para el servicio «{requiredAudience}».", StatusCode.PermissionDenied);
        if (!PlatformPrincipal.HasScope(user, requiredScope))
            throw Fail(StatusCode.PermissionDenied, $"El token no tiene el scope {requiredScope}.");
        if (!Guid.TryParse(tenantHeader, out var tenantId) || tenantId == Guid.Empty)
            throw Fail(StatusCode.InvalidArgument, $"Falta la metadata {TenantMetadata} con el id de la empresa.");

        return new PlatformServiceCaller(subject, tenantId);
    }

    private static RpcException Fail(StatusCode code, string detail) => new(new Status(code, detail));
}

/// <summary>
/// Convierte <see cref="PlatformException"/> en el estado gRPC del §10 con <c>ErrorInfo</c> (HU #13337 AC4). Los demás
/// errores siguen el camino normal de gRPC (un <see cref="RpcException"/> sale tal cual; lo inesperado, INTERNAL).
/// </summary>
public sealed class PlatformErrorInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (PlatformException ex)
        {
            throw PlatformRpcErrors.ToRpcException(ex.Code, ex.Message);
        }
    }
}
