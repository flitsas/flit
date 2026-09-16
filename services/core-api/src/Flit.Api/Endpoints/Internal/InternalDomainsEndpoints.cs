using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
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

        // HU #12425 — dominios que el poller ACME de #12426 debe atender: verified sin certificado
        // (primera emisión) o active con certificado próximo a vencer (renovación).
        app.MapGet("/api/v1/internal/domains/pending-certificate", GetPendingCertificateDomainsAsync)
            .WithName("InternalPendingCertificateDomains")
            .WithTags("Internal")
            .Produces<ActiveDomainsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        // HU #12425 (AC3) — señal de certificado emitido en el borde (#12426): verified → active.
        app.MapPut("/api/v1/internal/domains/{host}/certificate", PutCertificateAsync)
            .WithName("InternalDomainCertificate")
            .WithTags("Internal")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .AllowAnonymous();

        return app;
    }

    /// <summary>Ventana de renovación: certificados con <c>certificate_expires_at</c> dentro de 30 días entran igual que los dominios sin certificado emitido — el poller de #12426 los trata idéntico (pide/renueva).</summary>
    private static readonly TimeSpan CertificateRenewalWindow = TimeSpan.FromDays(30);

    internal static async Task<IResult> GetPendingCertificateDomainsAsync(
        HttpRequest request,
        [FromServices] ITenantDomainRepository repository,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration[ConfigKey];
        if (string.IsNullOrEmpty(expectedKey) || !HasValidKey(request, expectedKey))
        {
            return Results.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var hosts = await repository.ListPendingCertificateHostsAsync(now + CertificateRenewalWindow, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new ActiveDomainsResponse(hosts));
    }

    internal static async Task<IResult> PutCertificateAsync(
        string host,
        DomainCertificateRequestBody? body,
        HttpRequest request,
        [FromServices] ApplyDomainCertificateHandler handler,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration[ConfigKey];
        if (string.IsNullOrEmpty(expectedKey) || !HasValidKey(request, expectedKey))
        {
            return Results.Unauthorized();
        }

        if (body is null || body.IssuedAt == default)
        {
            return Results.Json(new { error = DomainErrors.HostInvalid, message = "issuedAt es requerido." }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler.HandleAsync(host, body.IssuedAt, body.ExpiresAt, cancellationToken).ConfigureAwait(false);
        return result.Outcome switch
        {
            Flit.Admin.Application.Companies.Domains.Verification.ApplyDomainCertificateOutcome.NotFound =>
                Results.NotFound(new { error = DomainErrors.NotFound, message = $"El host {host} no tiene dominio registrado." }),
            Flit.Admin.Application.Companies.Domains.Verification.ApplyDomainCertificateOutcome.NotVerified =>
                Results.Json(
                    new { error = DomainErrors.NotVerified, message = "El dominio aún no comprobó titularidad (pending/failed)." },
                    statusCode: StatusCodes.Status409Conflict),
            _ => Results.Ok(),
        };
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

/// <summary>Cuerpo de <c>PUT /api/v1/internal/domains/{host}/certificate</c> (HU #12425 AC3, #12426).</summary>
public sealed record DomainCertificateRequestBody(DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt);
