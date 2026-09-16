using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC1 — una compañía sin jerarquía (<see cref="MarcaBlancaScenario.Lone"/>) y una hija de
/// Concesión (<see cref="MarcaBlancaScenario.ConcesionChild"/>) deben comportarse EXACTAMENTE igual
/// que antes de la épica de Marca Blanca: marca resuelta = FLIT, correos idénticos byte a byte al
/// tema FLIT directo (nada que la resolución por BD pueda alterar) y CERO campos nuevos en las
/// respuestas HTTP existentes (<c>LoginResponse</c> sin red).
/// </summary>
public sealed class BrandParityTests(PostgresDatabaseFixture fixture) : MarcaBlancaHttpTestBase(fixture)
{
    [PostgresFact]
    public async Task AC1_ResolvePublicBranding_SinDominioDeRed_EsIdenticoAFlit()
    {
        var handler = new ResolvePublicBrandingHandler(
            new TenantBrandingRepository(NewContext()),
            new BrandingTenantLookupRepository(NewContext()),
            new NoopPublicBrandingCache(),
            NullLogger<ResolvePublicBrandingHandler>.Instance);

        var result = await handler.HandleAsync(isNetworkDomain: false, headTenantId: null, TestContext.Current.CancellationToken);

        result.Should().Be(BrandIdentityResponse.From(BrandIdentity.Flit));
    }

    [PostgresFact]
    public async Task AC1_DbEmailThemeResolver_HijaDeConcesionYSinRed_ResuelvenFlit()
    {
        var resolver = NewRealThemeResolver();

        var concesionChildTheme = await resolver.ResolveAsync(MarcaBlancaScenario.ConcesionChild, TestContext.Current.CancellationToken);
        var loneTheme = await resolver.ResolveAsync(MarcaBlancaScenario.Lone, TestContext.Current.CancellationToken);
        var concesionHeadTheme = await resolver.ResolveAsync(MarcaBlancaScenario.ConcesionHead, TestContext.Current.CancellationToken);

        concesionChildTheme.Should().Be(EmailTheme.Flit);
        loneTheme.Should().Be(EmailTheme.Flit);
        concesionHeadTheme.Should().Be(EmailTheme.Flit);
    }

    [PostgresFact]
    public async Task AC1_CorreoDeRecuperacion_ConTemaResueltoDeUnaCompaniaSinRed_EsByteAByteIgualAlTemaFlitDirecto()
    {
        var resolver = NewRealThemeResolver();
        var theme = await resolver.ResolveAsync(MarcaBlancaScenario.Lone, TestContext.Current.CancellationToken);

        var composedWithResolvedTheme = ForgotPasswordEmailTemplate.Compose(
            "Usuaria de prueba", "https://app.flit.test/password/reset?token=abc", 60, theme: theme);
        var composedWithFlitDirect = ForgotPasswordEmailTemplate.Compose(
            "Usuaria de prueba", "https://app.flit.test/password/reset?token=abc", 60, theme: EmailTheme.Flit);

        composedWithResolvedTheme.Subject.Should().Be(composedWithFlitDirect.Subject);
        composedWithResolvedTheme.HtmlBody.Should().Be(composedWithFlitDirect.HtmlBody);
    }

    [PostgresFact]
    public async Task AC1_LoginSinRed_RespuestaJson_SinCamposNuevos()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserLone),
                password = MarcaBlancaScenario.Password,
            }),
        };
        // Sin X-Flit-Domain: dominio de FLIT (comportamiento "de antes de la épica").
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        var keys = json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        keys.Should().BeEquivalentTo(["accessToken", "expiresInSeconds", "tokenType"],
            "una compañía sin red no debe recibir el campo aditivo \"network\" (AC1: sin campos nuevos)");
    }

    [PostgresFact]
    public async Task AC1_MeBranding_HijaDeConcesion_EsIdenticoAFlit()
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserConcesionChild),
                password = MarcaBlancaScenario.Password,
            }),
        };
        var loginResponse = await Client.SendAsync(loginRequest, TestContext.Current.CancellationToken);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>(TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/branding");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BrandIdentityResponse>(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be(BrandIdentityResponse.From(BrandIdentity.Flit));
    }

    private DbEmailThemeResolver NewRealThemeResolver() =>
        new(
            new BrandingTenantLookupRepository(NewContext()),
            new TenantBrandingRepository(NewContext()),
            new MemoryCache(new MemoryCacheOptions()),
            new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://dev.flitsas.online" },
            NullLogger<DbEmailThemeResolver>.Instance);

    /// <summary>Sin caché (cada prueba usa un <see cref="Microsoft.EntityFrameworkCore.DbContext"/> nuevo por operación, patrón del arnés).</summary>
    private sealed class NoopPublicBrandingCache : IPublicBrandingCache
    {
        public bool TryGet(Guid headTenantId, out BrandIdentity identity)
        {
            identity = BrandIdentity.Flit;
            return false;
        }

        public void Store(Guid headTenantId, BrandIdentity identity, TimeSpan ttl)
        {
        }
    }

    private sealed record LoginResponseDto(string AccessToken, int ExpiresInSeconds, string TokenType);
}
