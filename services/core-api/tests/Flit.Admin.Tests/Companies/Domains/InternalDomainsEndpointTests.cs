using Flit.Admin.Application.Companies.Domains;
using Flit.Api.Endpoints.Internal;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// HU #12417 AC3 — <see cref="InternalDomainsEndpoints.GetActiveDomainsAsync"/> sin levantar el
/// host completo. Uso de ejemplo:
/// <code>
/// var result = await InternalDomainsEndpoints.GetActiveDomainsAsync(request, resolver, configuration, ct);
/// </code>
/// </summary>
public sealed class InternalDomainsEndpointTests
{
    private const string ConfiguredKey = "clave-interna-de-prueba";

    private static ITenantDomainResolver NewResolver(params string[] hosts)
    {
        var resolver = Substitute.For<ITenantDomainResolver>();
        resolver.ListActiveHostsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)hosts);
        return resolver;
    }

    private static IConfiguration NewConfiguration(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null
                ? []
                : new Dictionary<string, string?> { ["Internal:ApiKey"] = apiKey })
            .Build();

    private static HttpRequest NewRequest(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers["X-Internal-Key"] = headerValue;
        }

        return context.Request;
    }

    // Results.Unauthorized()/Results.Ok(...) devuelven IStatusCodeHttpResult: se lee el código
    // directamente, sin ExecuteAsync (que exige un HttpContext.RequestServices completo con
    // opciones de serialización JSON — innecesario para esta prueba de contrato).
    private static int GetStatus(IResult result) =>
        result switch
        {
            IStatusCodeHttpResult statusCodeResult
                when statusCodeResult.StatusCode is { } statusCode => statusCode,
            _ => throw new InvalidOperationException($"El resultado {result.GetType()} no expone StatusCode."),
        };

    [Fact]
    public async Task SinClaveConfigurada_Responde401()
    {
        var result = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest(ConfiguredKey), NewResolver(), NewConfiguration(apiKey: null), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task SinCabeceraEnLaPeticion_Responde401()
    {
        var result = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest(null), NewResolver(), NewConfiguration(ConfiguredKey), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task ClaveIncorrecta_Responde401_MismoCodigoQueSinClave()
    {
        // "401/404 uniforme": el motivo (sin clave configurada, sin cabecera, clave incorrecta) NO
        // se distingue desde afuera — siempre el mismo código.
        var resultSinClave = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest(ConfiguredKey), NewResolver(), NewConfiguration(apiKey: null), TestContext.Current.CancellationToken);
        var resultClaveIncorrecta = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest("clave-equivocada"), NewResolver(), NewConfiguration(ConfiguredKey), TestContext.Current.CancellationToken);

        var statusSinClave = GetStatus(resultSinClave);
        var statusClaveIncorrecta = GetStatus(resultClaveIncorrecta);

        statusSinClave.Should().Be(statusClaveIncorrecta);
    }

    [Fact]
    public async Task ClaveDeLongitudDistinta_Responde401SinLanzar()
    {
        // FixedTimeEquals exige buffers del mismo tamaño; el guard de longitud debe cortar ANTES.
        var result = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest("corta"), NewResolver(), NewConfiguration(ConfiguredKey), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task ClaveCorrecta_Responde200ConLosHostsActivos()
    {
        var result = await InternalDomainsEndpoints.GetActiveDomainsAsync(
            NewRequest(ConfiguredKey),
            NewResolver("cliente.movilidadandina.com", "app.otrared.com"),
            NewConfiguration(ConfiguredKey),
            TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status200OK);
    }
}
