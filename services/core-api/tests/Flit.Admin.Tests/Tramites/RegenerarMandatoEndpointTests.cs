using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Flit.Api.Authorization;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Tramites;

/// <summary>
/// HU #14547 (AC5) — <c>POST /api/v1/admin/tramites/{id}/mandato/regenerar</c> es SOLO del rol SuperAdmin: la ruta
/// lleva su propia comprobación de rol porque <c>RequirePermission</c> solo hace bypass y no excluye a nadie. Un admin
/// con los 6 slugs de <c>admin-tramites-avanzado</c> recibe 403 sin que el repositorio se consulte.
/// <para>Uso de ejemplo: <c>await Client(Token("SuperAdmin")).PostAsync(Ruta, null, ct)</c> devuelve 404 si el trámite no existe.</para>
/// </summary>
public sealed class RegenerarMandatoEndpointTests : IClassFixture<RegenerarMandatoEndpointTests.RepoFactory>
{
    private static readonly Guid Tenant = Guid.Parse("c0000000-0000-4000-8000-000000014547");
    private static readonly Guid ProcedureId = Guid.Parse("c0000000-0001-4000-8000-000000014547");
    private static readonly string Ruta = $"/api/v1/admin/tramites/{ProcedureId}/mandato/regenerar";

    private readonly RepoFactory _factory;

    public RegenerarMandatoEndpointTests(RepoFactory factory)
    {
        _factory = factory;
        _factory.Repo.ClearReceivedCalls();
        _factory.Repo.GetByIdWithAttachmentsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProcedureInstance?)null);
    }

    [Fact]
    public async Task AC5_AdminConLosSeisSlugsDeGestionAvanzada_Recibe403_SinConsultarElTramite()
    {
        var response = await Client(Token("AdminCompany", conSlugs: true))
            .PostAsync(Ruta, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Repo.ReceivedCalls().Should().BeEmpty("la comprobación de rol corta antes del handler");
    }

    [Fact]
    public async Task AC5_SuperAdmin_PasaLaAutorizacion_YLlegaAlHandler()
    {
        var response = await Client(Token("SuperAdmin"))
            .PostAsync(Ruta, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "el repositorio no tiene el trámite");
        await _factory.Repo.Received().GetByIdWithAttachmentsAsync(ProcedureId, Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Contrato_SinHeaderXTenantId_Responde400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("SuperAdmin"));

        var response = await client.PostAsync(Ruta, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Anulado)]
    [InlineData(TramiteEstado.Revocado)]
    public async Task AC4_EnEstadoFinal_Responde409_ConElCodigoEstable(string estado)
    {
        _factory.Repo.GetByIdWithAttachmentsAsync(ProcedureId, Tenant, Arg.Any<CancellationToken>())
            .Returns(new ProcedureInstance { Id = ProcedureId, TenantId = Tenant, Status = estado, ReferenceNumber = "TRM-1" });

        var response = await Client(Token("SuperAdmin")).PostAsync(Ruta, null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("estado_no_permite_regenerar_mandato");
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant.ToString());
        return client;
    }

    private static string Token(string role, bool conSlugs = false)
    {
        var claims = new List<Claim>
        {
            new("sub", "33333333-3333-3333-3333-333333333333"),
            new("role", role),
            new("role_code", role),
            new("tenant_id", Tenant.ToString()),
        };
        if (conSlugs)
            claims.AddRange(AdminTramiteAuthorization.AllSlugs.Select(slug => new Claim("permissions", slug)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host con <see cref="IProcedureInstanceRepository"/> sustituido (sin PostgreSQL).</summary>
    public sealed class RepoFactory : WebApplicationFactory<Program>
    {
        public IProcedureInstanceRepository Repo { get; } = Substitute.For<IProcedureInstanceRepository>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.ConfigureTestServices(services => services.AddScoped(_ => Repo));
        }
    }
}
