using System.Net;
using System.Net.Http.Headers;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// HU #10588 — Provider RUES (mock por defecto) que resuelve la plantilla RUES_ACTOR_JURIDICAL
/// para actores persona jurídica (NIT). Feature #10583 (2ª ola).
/// </summary>
public sealed class VerifikRuesConsultationProviderTests
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static VerifikRuesConsultationProvider BuildProvider(string mode = "mock")
    {
        var http = new HttpClient { BaseAddress = new Uri("https://stub.example.com/") };
        var verifik = Options.Create(new VerifikOptions());
        var modes = Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = mode });
        return new VerifikRuesConsultationProvider(http, verifik, modes);
    }

    private static ConsultationContext ContextWithNit(string? nit) =>
        new(
            InstanceId,
            TenantId,
            "RUES_ACTOR_JURIDICAL",
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nit"] = nit,
            });

    [Fact]
    public void Key_EsVerifikRues()
    {
        BuildProvider().Key.Should().Be("verifik_rues");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_DevuelveGreenConCheckRuesOk()
    {
        // AC1: NIT (persona jurídica) → el provider responde con los datos del certificado.
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit("900123456"), ct);

        result.Provider.Should().Be("verifik_rues");
        result.Overall.Should().Be("green");
        result.Checks.Should().ContainSingle();
        result.Checks[0].Key.Should().Be("rues");
        result.Checks[0].Status.Should().Be("ok");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_HidrataDatosDeLaEmpresaYNitDelContexto()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit("900123456"), ct);

        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_razon_social");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_estado");
        result.HydratedFields.Should().ContainSingle(f => f.FieldKey == "rues_nit")
            .Which.ValueText.Should().Be("900123456");
    }

    [Fact]
    public async Task ConsultAsync_ModoMock_SinNit_NoHidrataNit()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = BuildProvider();

        var result = await provider.ConsultAsync(ContextWithNit(null), ct);

        result.Overall.Should().Be("green");
        result.HydratedFields.Should().NotContain(f => f.FieldKey == "rues_nit");
    }

    [Fact]
    public async Task ConsultAsync_ModoReal_EnviaV3ConCategoryRmYToken()
    {
        // Ajuste servicio RUES v3: ruta /v3/co/rues-complete, category=RM estático y Bearer token.
        var ct = TestContext.Current.CancellationToken;
        var handler = new CapturingHandler("""
            {"data":{"commercialRegistry":{"NIT":"901691963","businessName":"INVERSIONES ARCINIEGAS S.A.S.",
            "chamberCommerce":"BOGOTA","registrationNumber":"0003650415","registrationStatus":"ACTIVA"}},
            "signature":{"message":"Certified by Verifik.co"},"id":"4YB3V"}
            """);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.verifik.co/") };
        var verifik = Options.Create(new VerifikOptions { ApiToken = "env-token-123", AuthScheme = "Bearer" });
        var modes = Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = "real" });
        var provider = new VerifikRuesConsultationProvider(http, verifik, modes);

        var result = await provider.ConsultAsync(ContextWithNit("901691963"), ct);

        // La petición sale bien formada.
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v3/co/rues-complete");
        var query = handler.LastRequest.RequestUri.Query;
        query.Should().Contain("documentType=NIT");
        query.Should().Contain("documentNumber=901691963");
        query.Should().Contain("category=RM");
        handler.LastRequest.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "env-token-123"));

        // La respuesta v3 (data.commercialRegistry) se mapea a los campos hidratados.
        result.Overall.Should().Be("green");
        result.Checks[0].Status.Should().Be("ok");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_razon_social" && f.ValueText == "INVERSIONES ARCINIEGAS S.A.S.");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_estado" && f.ValueText == "ACTIVA");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_matricula_mercantil" && f.ValueText == "0003650415");
        result.HydratedFields.Should().Contain(f => f.FieldKey == "rues_camara_comercio" && f.ValueText == "BOGOTA");
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
