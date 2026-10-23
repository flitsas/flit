using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flit.DrFlit.Application.Abstractions;
using Flit.DrFlit.Application.Chat;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12931 AC3 — el filtro de consentimiento está ENLAZADO a las tres rutas reales del grupo
/// <c>/api/v1/dr-flit</c>: sin la versión vigente aceptada, <c>/chat</c>, <c>/support-cases</c> y
/// <c>/support-cases/attachments</c> responden 428 <c>consent_required</c> sin llamar al LLM ni a
/// Azure DevOps. Complementa <see cref="DrFlitConsentEndpointsTests"/>, que prueba la lógica del
/// filtro de forma unitaria: aquí lo que se fija es el cableado por ruta en el host real
/// (<c>WebApplicationFactory</c>, mismo patrón que los tests de telemetría).
/// </summary>
public sealed class DrFlitConsentPerRouteTests : IClassFixture<DrFlitConsentPerRouteTests.DrFlitConsentWebApplicationFactory>
{
    private const string Version = "2026-09-25";
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly DrFlitConsentWebApplicationFactory _factory;

    public DrFlitConsentPerRouteTests(DrFlitConsentWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.ConsentStore.ClearReceivedCalls();
        _factory.Assistant.ClearReceivedCalls();
        _factory.SupportGateway.ClearReceivedCalls();
    }

    public static TheoryData<string, string> RutasProtegidas => new()
    {
        { "/api/v1/dr-flit/chat", "json" },
        { "/api/v1/dr-flit/support-cases", "json" },
        { "/api/v1/dr-flit/support-cases/attachments", "multipart" },
    };

    [Theory]
    [MemberData(nameof(RutasProtegidas))]
    public async Task AC3_SinAceptacion_CadaRutaResponde428SinLlamarLlmNiAdo(string route, string contentKind)
    {
        _factory.ConsentStore.HasAcceptedAsync(UserId, Version, Arg.Any<CancellationToken>()).Returns(false);
        var client = NewAuthenticatedClient();

        var response = await client.PostAsync(route, ContentFor(contentKind), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired, $"la ruta {route} debe exigir consentimiento");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("code").GetString().Should().Be("consent_required");
        body.GetProperty("version").GetString().Should().Be(Version);
        await _factory.Assistant.DidNotReceiveWithAnyArgs().AskAsync(default!, TestContext.Current.CancellationToken);
        await _factory.SupportGateway.DidNotReceiveWithAnyArgs().CreateBugAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC3_ConAceptacion_ElChatSiLlegaAlAsistente()
    {
        _factory.ConsentStore.HasAcceptedAsync(UserId, Version, Arg.Any<CancellationToken>()).Returns(true);
        _factory.Assistant.AskAsync(Arg.Any<DrFlitChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DrFlitChatResult(DrFlitChatStatus.Ok, DrFlitIntent.Duda, "Así se hace.", [], 1, 30));
        var client = NewAuthenticatedClient();

        var response = await client.PostAsync(
            "/api/v1/dr-flit/chat", ContentFor("json"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("status").GetString().Should().Be("ok");
        await _factory.Assistant.ReceivedWithAnyArgs(1).AskAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("/api/v1/dr-flit/support-cases", "json-null")]
    [InlineData("/api/v1/dr-flit/support-cases/attachments", "multipart-sin-archivo")]
    public async Task AC3_ConAceptacion_SoporteYaNoDevuelve428(string route, string contentKind)
    {
        _factory.ConsentStore.HasAcceptedAsync(UserId, Version, Arg.Any<CancellationToken>()).Returns(true);
        var client = NewAuthenticatedClient();

        // Cuerpo sin datos a propósito: basta con probar que el filtro deja pasar (400 del endpoint,
        // nunca 428) sin tocar Azure DevOps ni el almacenamiento real de adjuntos.
        var response = await client.PostAsync(route, ContentFor(contentKind), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _factory.SupportGateway.DidNotReceiveWithAnyArgs().CreateBugAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(RutasProtegidas))]
    public async Task SinToken_401AntesQueCualquierOtraCosa(string route, string contentKind)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(route, ContentFor(contentKind), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.ConsentStore.DidNotReceiveWithAnyArgs().HasAcceptedAsync(default, default!, TestContext.Current.CancellationToken);
    }

    // ------------------------------------------------------------------
    // Infraestructura de la prueba
    // ------------------------------------------------------------------

    private static HttpContent ContentFor(string kind) => kind switch
    {
        "json" => JsonContent.Create(new { message = "¿cómo creo un traspaso?", history = Array.Empty<object>() }),
        "json-null" => JsonContent.Create<object?>(null),
        "multipart" => new MultipartFormDataContent
        {
            { new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "captura.pdf" },
        },
        "multipart-sin-archivo" => new MultipartFormDataContent(),
        _ => new StringContent(string.Empty),
    };

    private HttpClient NewAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantId.ToString());
        return client;
    }

    /// <summary>JWT dummy (mismo patrón que los tests de telemetría): el host de pruebas no
    /// configura llave pública, así que el token se acepta sin validar firma.</summary>
    private static string CreateToken()
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://api.flit.co",
            Audience = "flit-api",
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", UserId.ToString()),
                new Claim("role", "AdminCompany"),
                new Claim("tenant_id", TenantId.ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64))),
                SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Host real con el almacén de consentimiento, el asistente LLM y el gateway de ADO
    /// sustituidos, para poder afirmar "sin llamar al LLM ni a Azure DevOps" por ruta.</summary>
    public sealed class DrFlitConsentWebApplicationFactory : WebApplicationFactory<Program>
    {
        public IDrFlitConsentStore ConsentStore { get; } = Substitute.For<IDrFlitConsentStore>();
        public IDrFlitConsentSettings ConsentSettings { get; } = Substitute.For<IDrFlitConsentSettings>();
        public IDrFlitAssistant Assistant { get; } = Substitute.For<IDrFlitAssistant>();
        public IDrFlitSupportCaseGateway SupportGateway { get; } = Substitute.For<IDrFlitSupportCaseGateway>();

        public DrFlitConsentWebApplicationFactory()
        {
            ConsentSettings.CurrentVersion.Returns(Version);
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDrFlitConsentStore>();
                services.AddSingleton(ConsentStore);
                services.RemoveAll<IDrFlitConsentSettings>();
                services.AddSingleton(ConsentSettings);
                services.RemoveAll<IDrFlitAssistant>();
                services.AddSingleton(Assistant);
                services.RemoveAll<IDrFlitSupportCaseGateway>();
                services.AddSingleton(SupportGateway);
            });
        }
    }
}
