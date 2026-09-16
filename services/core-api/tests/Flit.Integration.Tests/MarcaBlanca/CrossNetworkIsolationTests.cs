using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC4 — aislamiento entre la red A y la red B (dos cabezas MARCA_BLANCA con marca y
/// dominio propios, <see cref="MarcaBlancaScenario"/>): un usuario de A nunca abre sesión por el
/// dominio de B ni directamente por el dominio de FLIT (redirige, HU #12422 AC3); <c>/public/branding</c>
/// nunca cruza marca entre dominios, incluso calentando/invalidando las cachés en orden cruzado
/// (A, B, A); y <c>/me/branding</c> de una hija de A hereda la marca de A.
/// </summary>
public sealed class CrossNetworkIsolationTests(PostgresDatabaseFixture fixture) : MarcaBlancaHttpTestBase(fixture)
{
    [PostgresFact]
    public async Task AC4_UsuarioDeA_NoEntraPorElDominioDeB()
    {
        var response = await LoginAsync(
            MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadA),
            MarcaBlancaScenario.Password,
            domain: MarcaBlancaScenario.HostB);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PostgresFact]
    public async Task AC4_UsuarioDeA_NoEntraDirectamentePorElDominioDeFlit_RedirigeASuPropioDominio()
    {
        var response = await LoginAsync(
            MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadA),
            MarcaBlancaScenario.Password,
            domain: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("NETWORK_DOMAIN_REQUIRED").And.Contain(MarcaBlancaScenario.HostA);
    }

    [PostgresFact]
    public async Task AC4_PublicBranding_NuncaCruzaMarcaEntreDominios_EnOrdenCruzadoAByA()
    {
        var firstA = await PublicBrandingAsync(MarcaBlancaScenario.HostA);
        var firstB = await PublicBrandingAsync(MarcaBlancaScenario.HostB);
        var secondA = await PublicBrandingAsync(MarcaBlancaScenario.HostA);

        firstA.Should().Contain(MarcaBlancaScenario.PlatformNameA).And.NotContain(MarcaBlancaScenario.PlatformNameB);
        firstB.Should().Contain(MarcaBlancaScenario.PlatformNameB).And.NotContain(MarcaBlancaScenario.PlatformNameA);
        secondA.Should().Contain(MarcaBlancaScenario.PlatformNameA).And.NotContain(MarcaBlancaScenario.PlatformNameB);
        secondA.Should().Be(firstA, "la caché de 60s recalentada tras B no debe alterar la respuesta de A");
    }

    [PostgresFact]
    public async Task AC4_MeBranding_HijaDeA_HeredaLaMarcaDeA()
    {
        var loginResponse = await LoginAsync(
            MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserChildA),
            MarcaBlancaScenario.Password,
            domain: MarcaBlancaScenario.HostA);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/branding");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        // HU #12422 AC5 (DomainBindingMiddleware) — el token quedó ligado al dominio de A (claim
        // "dom"); sin el mismo sello en esta petición, SESSION_DOMAIN_MISMATCH la rechazaría con 401
        // ANTES de llegar al handler — no es lo que AC4 quiere ejercitar aquí (herencia de marca).
        request.Headers.Add("X-Flit-Domain", MarcaBlancaScenario.HostA);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(MarcaBlancaScenario.PlatformNameA).And.NotContain(MarcaBlancaScenario.PlatformNameB);
    }

    private async Task<HttpResponseMessage> LoginAsync(string email, string password, string? domain)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        if (domain is not null)
        {
            request.Headers.Add("X-Flit-Domain", domain);
        }

        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private async Task<string> PublicBrandingAsync(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/branding");
        request.Headers.Add("X-Flit-Domain", host);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private sealed record LoginResponseDto(string AccessToken, int ExpiresInSeconds, string TokenType);
}
