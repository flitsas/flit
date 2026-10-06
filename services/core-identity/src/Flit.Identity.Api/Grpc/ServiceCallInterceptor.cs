using System.Security.Claims;
using Flit.Api.Identity;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Flit.Identity.Api.Grpc;

/// <summary>Quién llama y por qué empresa, ya validado por <see cref="ServiceCallInterceptor"/>.</summary>
internal sealed record ServiceCaller(string ClientId, Guid TenantId)
{
    private const string Key = "flit.serviceCaller";

    public static ServiceCaller From(ServerCallContext context) =>
        context.UserState.TryGetValue(Key, out var value) && value is ServiceCaller caller
            ? caller
            : throw new RpcException(new Status(StatusCode.Internal, "La llamada no pasó por la validación del servicio."));

    public void Store(ServerCallContext context) => context.UserState[Key] = this;
}

/// <summary>
/// Validación de una llamada gRPC entre servicios (contrato de plataforma v1.3 §3 y §6.1, ADR-0070), local a
/// core-identity hasta que el SDK traiga su interceptor de servidor (HU #13337), que la reemplaza:
/// <list type="bullet">
///   <item>Sin token, o con token de usuario: <c>UNAUTHENTICATED</c>. Un token de usuario nunca puede elegir empresa.</item>
///   <item>Token de servicio sin la audiencia de este servicio o sin el scope del método: <c>PERMISSION_DENIED</c>.</item>
///   <item>Sin <c>x-flit-tenant-id</c>, o que no es un UUID: <c>INVALID_ARGUMENT</c>.</item>
/// </list>
/// La firma, el emisor y la vigencia ya los validó la autenticación de ASP.NET Core (la misma que acepta el manifiesto).
/// Un token de servicio se reconoce porque su <c>sub</c> es el cliente (<c>svc-…</c>) y no un usuario (UUID).
/// </summary>
internal sealed class ServiceCallInterceptor(string requiredScope, string requiredAudience) : Interceptor
{
    public const string TenantHeader = "x-flit-tenant-id";

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);
        Validate(context.GetHttpContext().User, context.RequestHeaders.GetValue(TenantHeader)).Store(context);
        return await continuation(request, context).ConfigureAwait(false);
    }

    internal ServiceCaller Validate(ClaimsPrincipal user, string? tenantHeader)
    {
        var subject = user.FindFirstValue("sub");
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(subject))
            throw Fail(StatusCode.Unauthenticated, "Falta un token de servicio válido.");
        if (!IsServiceSubject(subject))
            throw Fail(StatusCode.Unauthenticated, "Esta llamada solo acepta tokens de servicio.");
        if (!user.FindAll("aud").Any(c => c.Value == requiredAudience))
            throw Fail(StatusCode.PermissionDenied, $"El token no es para el servicio «{requiredAudience}».");
        if (!HasScope(user, requiredScope))
            throw Fail(StatusCode.PermissionDenied, $"El token no tiene el scope {requiredScope}.");
        if (!Guid.TryParse(tenantHeader, out var tenantId) || tenantId == Guid.Empty)
            throw Fail(StatusCode.InvalidArgument, $"Falta la metadata {TenantHeader} con el id de la empresa.");

        return new ServiceCaller(subject, tenantId);
    }

    /// <summary>Clientes de servicio del contrato §3 (<c>svc-&lt;código&gt;</c>); los usuarios tienen un UUID.</summary>
    internal static bool IsServiceSubject(string subject) =>
        subject.StartsWith(OidcDefaults.ServiceClientPrefix, StringComparison.Ordinal) && !Guid.TryParse(subject, out _);

    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);

    private static RpcException Fail(StatusCode code, string detail) => new(new Status(code, detail));
}
