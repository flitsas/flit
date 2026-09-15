using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.Companies.Domains;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints.Internal;

/// <summary>
/// Consumo EXCLUSIVO servicio a servicio (HU #12417 AC3, ADR-0060 D2): <c>Flit.Gateway</c> (sin BD
/// propia) lo llama DIRECTO — fuera de YARP, la ruta pública <c>/api/v1/internal/*</c> se bloquea
/// en el propio Gateway — para derivar los orígenes CORS dinámicos (<c>DynamicCorsOriginSource</c>).
/// Autenticado con <c>X-Internal-Key</c> (clave compartida <c>Internal:ApiKey</c>), NUNCA JWT.
/// Comparación en tiempo constante; sin la clave configurada o con una clave incorrecta responde
/// SIEMPRE 401 de forma indistinguible (fail-closed, sin revelar cuál fue el motivo).
/// </summary>
public static class InternalDomainsEndpoints
{
    private const string InternalKeyHeader = "X-Internal-Key";
    private const string ConfigKey = "Internal:ApiKey";

    public static IEndpointRouteBuilder MapInternalDomainsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/internal/domains/active", GetActiveDomainsAsync)
            .WithName("InternalActiveDomains")
            .WithTags("Internal")
            .Produces<ActiveDomainsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        return app;
    }

    internal static async Task<IResult> GetActiveDomainsAsync(
        HttpRequest request,
        [FromServices] ITenantDomainResolver resolver,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration[ConfigKey];
        if (string.IsNullOrEmpty(expectedKey) || !HasValidKey(request, expectedKey))
        {
            return Results.Unauthorized();
        }

        var hosts = await resolver.ListActiveHostsAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(new ActiveDomainsResponse(hosts));
    }

    private static bool HasValidKey(HttpRequest request, string expectedKey)
    {
        if (!request.Headers.TryGetValue(InternalKeyHeader, out var provided) || provided.Count == 0)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedValue = provided.ToString();
        var providedBytes = Encoding.UTF8.GetBytes(providedValue);

        // Longitudes distintas se rechazan ANTES de comparar (FixedTimeEquals exige buffers del
        // mismo tamaño) — mismo patrón que Argon2PasswordHasher.VerifyHash.
        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}

/// <summary>Forma de <c>GET /api/v1/internal/domains/active</c> (HU #12417 AC3).</summary>
public sealed record ActiveDomainsResponse(IReadOnlyList<string> Hosts);
