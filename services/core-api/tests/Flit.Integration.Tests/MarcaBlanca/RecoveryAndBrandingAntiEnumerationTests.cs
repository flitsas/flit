using System.Net;
using System.Net.Http.Json;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC3 — dos rutas anti-enumeración por el dominio de la red A
/// (<c>X-Flit-Domain: <see cref="MarcaBlancaScenario.HostA"/></c>):
/// <list type="bullet">
///   <item><c>POST /api/v1/auth/forgot-password</c> con los mismos tres sujetos de AC2 (usuario de A,
///   usuario de B, correo inexistente) — SIEMPRE 202 con el mismo cuerpo genérico.</item>
///   <item><c>GET /api/v1/public/branding</c> con un dominio inexistente, uno <c>verified</c> (no
///   activo) y uno de una cabeza MARCA_BLANCA inactiva — mismo cuerpo (FLIT), mismo código 200 y
///   tiempo dentro de la tolerancia.</item>
/// </list>
/// </summary>
[Trait("Category", "Timing")]
public sealed class RecoveryAndBrandingAntiEnumerationTests(PostgresDatabaseFixture fixture) : MarcaBlancaHttpTestBase(fixture)
{
    [PostgresFact]
    public async Task AC3_ForgotPassword_TresSujetos_MismaRespuesta()
    {
        var userOfA = await ForgotPasswordAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadA));
        var userOfB = await ForgotPasswordAsync(MarcaBlancaScenario.EmailOf(MarcaBlancaScenario.UserHeadB));
        var nonexistent = await ForgotPasswordAsync("no-existe-jamas@flit.test");

        userOfA.Status.Should().Be(HttpStatusCode.Accepted);
        userOfB.Status.Should().Be(HttpStatusCode.Accepted);
        nonexistent.Status.Should().Be(HttpStatusCode.Accepted);

        userOfB.Body.Should().Be(userOfA.Body);
        nonexistent.Body.Should().Be(userOfA.Body);
    }

    [PostgresFact]
    public async Task AC3_PublicBranding_DominioInexistenteVerificadoEInactivo_MismoCuerpoYCodigo()
    {
        var (verifiedHost, inactiveHost) = await SeedNegativeDomainsAsync();

        var inexistente = await PublicBrandingAsync("no-registrado-jamas.example");
        var verificadoNoActivo = await PublicBrandingAsync(verifiedHost);
        var cabezaInactiva = await PublicBrandingAsync(inactiveHost);

        inexistente.Status.Should().Be(HttpStatusCode.OK);
        verificadoNoActivo.Status.Should().Be(HttpStatusCode.OK);
        cabezaInactiva.Status.Should().Be(HttpStatusCode.OK);

        verificadoNoActivo.Body.Should().Be(inexistente.Body);
        cabezaInactiva.Body.Should().Be(inexistente.Body);
        inexistente.Body.Should().Contain("\"platformName\":\"FLIT 2.0\"");
    }

    [PostgresFact]
    public async Task AC3_PublicBranding_TiempoEntreCasosNegativos_QuedaDentroDeLaTolerancia()
    {
        var (verifiedHost, inactiveHost) = await SeedNegativeDomainsAsync();

        var medianInexistente = await ResponseTimingSampler.MedianMsAsync(() => PublicBrandingRawAsync("no-registrado-jamas.example"));
        var medianVerificado = await ResponseTimingSampler.MedianMsAsync(() => PublicBrandingRawAsync(verifiedHost));
        var medianInactivo = await ResponseTimingSampler.MedianMsAsync(() => PublicBrandingRawAsync(inactiveHost));

        Math.Abs(medianVerificado - medianInexistente).Should().BeLessOrEqualTo(ResponseTimingSampler.ToleranceMs);
        Math.Abs(medianInactivo - medianInexistente).Should().BeLessOrEqualTo(ResponseTimingSampler.ToleranceMs);
    }

    /// <summary>
    /// Dos cabezas MARCA_BLANCA adicionales al escenario base: una con dominio <c>verified</c> (nunca
    /// llegó a <c>active</c>: sin certificado) y otra con dominio <c>active</c> pero <c>is_active =
    /// false</c> — ambas quedan FUERA de <c>admin.v_active_network_domains</c> (DDL 116), así que
    /// <c>DomainContextMiddleware</c> ya las resuelve como dominio de FLIT antes de que el handler
    /// tenga que aplicar su propia relectura de respaldo (AC2/AC7 de #12418).
    /// </summary>
    private async Task<(string VerifiedHost, string InactiveHost)> SeedNegativeDomainsAsync()
    {
        var verifiedHeadId = Guid.NewGuid();
        var inactiveHeadId = Guid.NewGuid();
        const string verifiedHost = "app.red-verificada-it.example";
        const string inactiveHost = "app.red-inactiva-it.example";

        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        ctx.Tenants.Add(NewMarcaBlancaHead(verifiedHeadId, "IT-MB-VERIFIED", isActive: true));
        ctx.Tenants.Add(NewMarcaBlancaHead(inactiveHeadId, "IT-MB-INACTIVE", isActive: false));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        ctx.TenantDomains.AddRange(
            new TenantDomainEntity
            {
                TenantId = verifiedHeadId,
                Host = verifiedHost,
                VerificationToken = "tok-verified-0000000000001",
                Status = TenantDomainStatuses.Verified,
                VerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new TenantDomainEntity
            {
                TenantId = inactiveHeadId,
                Host = inactiveHost,
                VerificationToken = "tok-inactive-0000000000002",
                Status = TenantDomainStatuses.Active,
                VerifiedAt = now,
                ActivatedAt = now,
                CertificateIssuedAt = now,
                CertificateExpiresAt = now.AddDays(90),
                CreatedAt = now,
                UpdatedAt = now,
            });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (verifiedHost, inactiveHost);
    }

    private static Flit.Infrastructure.Persistence.Entities.Identity.Tenant NewMarcaBlancaHead(Guid id, string code, bool isActive)
    {
        var tenant = Postgres.TenantSeed.New(id, code, isGroupParent: true, parentId: null, Flit.Admin.Domain.Companies.Create.HeadTenantTypes.MarcaBlanca);
        tenant.TaxId = $"7{id:N}"[..15];
        tenant.IsActive = isActive;
        return tenant;
    }

    private async Task<(HttpStatusCode Status, string Body)> ForgotPasswordAsync(string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/forgot-password")
        {
            Content = JsonContent.Create(new { email }),
        };
        request.Headers.Add("X-Flit-Domain", MarcaBlancaScenario.HostA);
        var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, body);
    }

    private async Task<(HttpStatusCode Status, string Body)> PublicBrandingAsync(string host)
    {
        var response = await PublicBrandingRawAsync(host);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, body);
    }

    private async Task<HttpResponseMessage> PublicBrandingRawAsync(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/branding");
        request.Headers.Add("X-Flit-Domain", host);
        return await Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
