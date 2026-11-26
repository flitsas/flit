using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Platform;
using Flit.Modules.Platform.Application.TenantProducts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Platform;

/// <summary>
/// HU #14711 (AC-DC-01, AC-DC-05, AC-DC-12; guía de integración §3) — el cliente del alta del proveedor: cuerpo exacto
/// (<c>legal_name</c>, <c>tax_id</c>, <c>contact_email</c>) con <c>Authorization: Bearer</c>, 200/201 con las credenciales,
/// 400/401/404/415 a códigos propios sin reintento, 5xx/429/timeout con UN reintento y sin token <c>PROVEEDOR_NO_CONFIGURADO</c>
/// sin llamar. Nunca deja el token ni la llave en un mensaje.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <c>var r = await cliente.DarDeAltaAsync(new PeticionAltaProveedor("Flota Andina SAS", "900123456", "ops@flota.example"), ct);</c>.
/// </remarks>
public sealed class ClienteAltaProveedorImpuestoTests
{
    private const string Base = "https://proveedor.prueba.local";
    private const string Token = "token-de-integracion-CENTINELA-0123456789abcdef";
    private const string Llave = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_abcde";
    private const string Secreto = "whsec_CENTINELA_0123456789"; // gitleaks:allow
    private static readonly PeticionAltaProveedor Peticion = new("Flota Andina SAS", "900123456", "ops@flota.example");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Alta(bool created, bool rotated = false, string? secreto = Secreto) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["company"] = new Dictionary<string, object?> { ["id"] = "3f6c2a7e-6a0b-4c8e-9a51-2f3b5d7c9e10", ["tax_id"] = "900123456", ["tax_id_dv"] = 8, ["active"] = true, ["api_key_prefix"] = "AbCdEfGh", ["has_webhook_secret"] = true },
        ["created"] = created,
        ["api_key"] = Llave,
        ["api_key_rotated"] = rotated,
        ["webhook_secret"] = secreto,
    });

    private static string Error(string code) => "{\"error\":{\"code\":\"" + code + "\",\"message\":\"x\"}}";

    private static (ClienteAltaProveedorImpuesto Cliente, ProveedorDePrueba Proveedor) Crear(
        Func<int, HttpResponseMessage> responder, string? token = Token, string? baseUrl = Base)
    {
        var proveedor = new ProveedorDePrueba(responder);
        var http = new HttpClient(proveedor) { Timeout = TimeSpan.FromSeconds(30) };
        var opciones = Options.Create(new OpcionesProveedorImpuesto { BaseUrl = baseUrl, IntegrationToken = token, TimeoutSeconds = 30 });
        return (new ClienteAltaProveedorImpuesto(http, opciones), proveedor);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string cuerpo) =>
        new(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Alta201_DevuelveLasCredenciales_YEnviaElCuerpoExactoConElBearer()
    {
        var (cliente, proveedor) = Crear(_ => Json(HttpStatusCode.Created, Alta(created: true)));

        var r = await cliente.DarDeAltaAsync(Peticion, Ct);

        r.Should().Be(new RespuestaAltaProveedor(Llave, Secreto, Created: true, ApiKeyRotated: false));
        var pedido = proveedor.Pedidos.Single();
        pedido.Metodo.Should().Be(HttpMethod.Post);
        pedido.Uri.Should().Be(Base + "/api/v1/integracion/empresas");
        pedido.Autorizacion.Should().Be("Bearer " + Token);
        pedido.TipoContenido.Should().StartWith("application/json");
        using var cuerpo = JsonDocument.Parse(pedido.Cuerpo);
        cuerpo.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString()))
            .Should().BeEquivalentTo([("legal_name", "Flota Andina SAS"), ("tax_id", "900123456"), ("contact_email", "ops@flota.example")]);
    }

    [Fact]
    public async Task Alta200_EmpresaQueYaExistia_TraeLasMismasCredenciales_YLaRotacion()
    {
        var (cliente, _) = Crear(_ => Json(HttpStatusCode.OK, Alta(created: false, rotated: true)));

        var r = await cliente.DarDeAltaAsync(Peticion, Ct);

        r.Should().Be(new RespuestaAltaProveedor(Llave, Secreto, Created: false, ApiKeyRotated: true));
    }

    [Theory]
    [InlineData(400, "bad_request", "PROVEEDOR_SOLICITUD_INVALIDA")]
    [InlineData(401, "unauthorized", "PROVEEDOR_TOKEN_INVALIDO")]
    [InlineData(404, "not_found", "PROVEEDOR_INTEGRACION_DESHABILITADA")]
    [InlineData(415, "unsupported_media_type", "PROVEEDOR_SOLICITUD_INVALIDA")]
    public async Task Errores4xx_SeMapeanSinReintento(int status, string codigoProveedor, string esperado)
    {
        var (cliente, proveedor) = Crear(_ => Json((HttpStatusCode)status, Error(codigoProveedor)));

        var act = () => cliente.DarDeAltaAsync(Peticion, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(esperado);
        proveedor.Pedidos.Should().ContainSingle("un 4xx no se reintenta");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(0)]
    public async Task Fallo5xxO429OTimeout_SeReintentaUnaVez_YElReintentoDaLasCredenciales(int primera)
    {
        var (cliente, proveedor) = Crear(n => n == 1
            ? primera == 0 ? throw new TaskCanceledException("timeout de prueba") : Json((HttpStatusCode)primera, Error("x"))
            : Json(HttpStatusCode.OK, Alta(created: false)));

        var r = await cliente.DarDeAltaAsync(Peticion, Ct);

        r.ApiKey.Should().Be(Llave);
        proveedor.Pedidos.Should().HaveCount(2);
        proveedor.Pedidos[1].Cuerpo.Should().Be(proveedor.Pedidos[0].Cuerpo, "el reintento va con el mismo cuerpo");
    }

    [Theory]
    [InlineData(502)]
    [InlineData(0)]
    public async Task DosFallosSeguidos_ProveedorNoDisponible(int status)
    {
        var (cliente, proveedor) = Crear(_ => status == 0 ? throw new HttpRequestException("sin red") : Json((HttpStatusCode)status, Error("x")));

        var act = () => cliente.DarDeAltaAsync(Peticion, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.ProveedorNoDisponible);
        proveedor.Pedidos.Should().HaveCount(2);
    }

    [Fact]
    public async Task Respuesta2xxSinLlave_ProveedorNoDisponible()
    {
        var (cliente, _) = Crear(_ => Json(HttpStatusCode.OK, "{\"company\":{\"id\":\"x\"},\"created\":false}"));

        var act = () => cliente.DarDeAltaAsync(Peticion, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.ProveedorNoDisponible);
    }

    [Theory]
    [InlineData(null, Base)]
    [InlineData("", Base)]
    [InlineData(Token, null)]
    [InlineData(Token, "")]
    public async Task SinTokenOSinBase_ProveedorNoConfigurado_SinLlamar(string? token, string? baseUrl)
    {
        var (cliente, proveedor) = Crear(_ => Json(HttpStatusCode.Created, Alta(created: true)), token, baseUrl);

        var act = () => cliente.DarDeAltaAsync(Peticion, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.ProveedorNoConfigurado);
        proveedor.Pedidos.Should().BeEmpty();
    }

    // ── L-2: https obligatorio salvo loopback ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://proveedor.prueba.local")]
    [InlineData("http://10.0.0.5:8898")]
    [InlineData("ftp://proveedor.prueba.local")]
    public async Task BaseSinHttpsFueraDeLoopback_ProveedorNoConfigurado_SinEnviarElBearer(string baseUrl)
    {
        var (cliente, proveedor) = Crear(_ => Json(HttpStatusCode.Created, Alta(created: true)), Token, baseUrl);

        var act = () => cliente.DarDeAltaAsync(Peticion, Ct);

        (await act.Should().ThrowAsync<AltaCredencialImpuestoException>()).Which.Code.Should().Be(CodigosAltaCredencialImpuesto.ProveedorNoConfigurado);
        proveedor.Pedidos.Should().BeEmpty("el Bearer no sale por un canal sin cifrar");
    }

    [Theory]
    [InlineData("http://127.0.0.1:8898")]
    [InlineData("http://localhost:8898")]
    [InlineData("http://[::1]:8898")]
    public async Task HttpEnLoopback_SePermite_ParaElStubLocal(string baseUrl)
    {
        var (cliente, proveedor) = Crear(_ => Json(HttpStatusCode.Created, Alta(created: true)), Token, baseUrl);

        await cliente.DarDeAltaAsync(Peticion, Ct);

        proveedor.Pedidos.Should().ContainSingle();
    }

    [Fact]
    public async Task NingunErrorLlevaElTokenNiLaLlave()
    {
        var (cliente, _) = Crear(_ => Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"" + Token + " " + Llave + "\"}}"));

        var error = (await FluentActions.Awaiting(() => cliente.DarDeAltaAsync(Peticion, Ct)).Should().ThrowAsync<AltaCredencialImpuestoException>()).Which;

        error.ToString().Should().NotContain(Token).And.NotContain(Llave);
        new OpcionesProveedorImpuesto { IntegrationToken = Token }.ToString().Should().NotContain(Token);
        new RespuestaAltaProveedor(Llave, Secreto, true, false).ToString().Should().NotContain(Llave).And.NotContain(Secreto);
        Peticion.ToString().Should().NotContain("ops@flota.example");
    }

    [Fact]
    public void Registro_SinConfiguracion_ArrancaSinLanzar_YElClienteSeResuelve()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        services.AddClienteAltaProveedorImpuesto(config);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IClienteAltaProveedorImpuesto>().Should().NotBeNull();
        sp.GetRequiredService<IOptions<OpcionesProveedorImpuesto>>().Value.TimeoutSeconds.Should().Be(30);
    }

    private sealed class ProveedorDePrueba(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Uri, string? Autorizacion, string? TipoContenido, string Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), request.Content?.Headers.ContentType?.ToString(), cuerpo));
            return responder(Pedidos.Count);
        }
    }
}
