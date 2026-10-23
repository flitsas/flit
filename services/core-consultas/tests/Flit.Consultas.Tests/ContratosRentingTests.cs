using System.Net;
using System.Text;
using Flit.Consultas.Api.Comparendos;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13712 — API de contratos de Renting. Renting es falsa (no sale a la red): la placa decide la respuesta. El
/// cableado por gRPC y el consumo están en <see cref="FuentesComparendosTests"/>.
/// </summary>
public sealed class ContratosRentingTests
{
    internal const string Llave = "LLAVE-SUSCRIPCION-PRUEBA";
    private static readonly DateOnly Fecha = new(2026, 10, 18);

    internal static Dictionary<string, string?> Config(bool contactoReal = false) => new()
    {
        ["RENTING_API_BASE_URL"] = "https://renting.prueba",
        ["RENTING_API_KEY_NAME"] = "Ocp-Apim-Subscription-Key",
        ["RENTING_API_KEY_VALUE"] = Llave,
        ["RENTING_API_PFX_CERTIFICATE_PATH"] = "/no/se/abre/en/estas/pruebas.pfx",
        ["RENTING_API_PASSPHRASE"] = "x",
        ["RENTING_API_LOGIN_PATH"] = "/Authorization/TokenForThird",
        ["RENTING_API_LOGIN_SECRET_NAME"] = "ThirdParties",
        ["RENTING_API_LOGIN_SUBJECT"] = "CN=prueba",
        ["RENTING_API_CONTRATOS_PATH"] = "/RentingMaster/ContractPlate/GetContractPlatesInfoByPlate",
        ["RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL"] = "buzon.pruebas@flitsas.com",
        ["RENTING_API_CONTRATOS_REAL_CONTACT_ENABLED"] = contactoReal ? "true" : null,
    };

    private static (ProveedorContratosRenting Proveedor, RentingFalsa Renting, RelojFalso Reloj) Crear(bool contactoReal = false)
    {
        var renting = new RentingFalsa();
        var reloj = new RelojFalso();
        var opciones = RentingContratosOpciones.Desde(new ConfigurationBuilder().AddInMemoryCollection(Config(contactoReal)).Build());
        return (new ProveedorContratosRenting(new Fabrica(renting), opciones, null, reloj), renting, reloj);
    }

    [Fact]
    public async Task AC1_Encontrado_LoginYConsultaConLaLlave_YElContactoDesviado()
    {
        var (p, renting, _) = Crear();
        var r = await p.ConsultarAsync("lzo227", Fecha, TestContext.Current.CancellationToken);

        r.Resultado.Should().Be(ResultadoContratoConductor.Encontrado);
        var k = r.Conductor;
        (k.TipoDocumento, k.NumeroDocumento, k.Nombres, k.Ciudad).Should().Be(("CC", "1020304050", "ANA MARIA PEREZ GOMEZ", "MEDELLIN"));
        (k.Correo, k.HasTelefono, r.ContactoDesviado).Should().Be(("buzon.pruebas@flitsas.com", false, true),
            "Renting es PRODUCCIÓN: fuera de PDN los avisos nunca le llegan al cliente");

        renting.Logins.Should().Be(1);
        renting.Pedidos.Should().OnlyContain(x => x.Llave == Llave, "la llave de suscripción va en el login y en la consulta");
        renting.Pedidos[0].Cuerpo.Should().Be("""{"secretName":"ThirdParties","subject":"CN=prueba"}""");
        var consulta = renting.Pedidos[1];
        consulta.Url.Should().Be("https://renting.prueba/RentingMaster/ContractPlate/GetContractPlatesInfoByPlate?plate=LZO227&offenseDate=2026-10-18T12%3A00%3A00.000Z");
        consulta.Bearer.Should().Be("token-1");
    }

    [Fact]
    public async Task AC1_ContactoReal_SoloConElInterruptor()
    {
        var (p, _, _) = Crear(contactoReal: true);
        var r = await p.ConsultarAsync("LZO227", Fecha, TestContext.Current.CancellationToken);
        (r.Conductor.Correo, r.Conductor.Telefono, r.ContactoDesviado).Should().Be(("ana.perez@cliente.test", "3001112233", false));
    }

    [Fact]
    public async Task AC1_ElTokenSeReutiliza_YSeRenuevaAlVencer()
    {
        var (p, renting, reloj) = Crear();
        await p.ConsultarAsync("LZO227", Fecha, TestContext.Current.CancellationToken);
        await p.ConsultarAsync("ESO155", Fecha, TestContext.Current.CancellationToken);
        renting.Logins.Should().Be(1);

        reloj.Advance(TimeSpan.FromSeconds(601));
        await p.ConsultarAsync("LZO227", Fecha, TestContext.Current.CancellationToken);
        renting.Logins.Should().Be(2);
    }

    [Fact]
    public async Task AC1_Un401EnLaConsulta_PideOtroTokenUnaVez()
    {
        var (p, renting, _) = Crear();
        var r = await p.ConsultarAsync("TOK401", Fecha, TestContext.Current.CancellationToken);
        r.Resultado.Should().Be(ResultadoContratoConductor.Encontrado);
        renting.Logins.Should().Be(2);
    }

    [Theory]
    [InlineData("ARR000")]
    [InlineData("NOF404")]
    [InlineData("NUL000")]
    [InlineData("SIN000")]
    public async Task AC2_SinContrato_EsNoEncontrado(string placa)
    {
        var (p, _, _) = Crear();
        var r = await p.ConsultarAsync(placa, Fecha, TestContext.Current.CancellationToken);
        r.Resultado.Should().Be(ResultadoContratoConductor.NoEncontrado);
        r.HasMensajeError.Should().BeFalse();
    }

    [Theory]
    [InlineData("ERR500", TipoErrorFuente.HttpNoExitoso)]
    [InlineData("ERR403", TipoErrorFuente.Autenticacion)]
    [InlineData("HTM200", TipoErrorFuente.RespuestaInvalida)]
    [InlineData("NUM200", TipoErrorFuente.RespuestaInvalida)]
    [InlineData("RED000", TipoErrorFuente.Red)]
    public async Task AC3_ErrorNuncaEsNoEncontrado_YNoLlevaLaLlave(string placa, TipoErrorFuente tipo)
    {
        var (p, _, _) = Crear();
        var r = await p.ConsultarAsync(placa, Fecha, TestContext.Current.CancellationToken);
        (r.Resultado, r.TipoError).Should().Be((ResultadoContratoConductor.Error, tipo));
        r.MensajeError.Should().NotContain(Llave).And.NotContain("token-");
    }

    [Fact]
    public async Task AC3_LoginRechazado_EsErrorDeAutenticacion()
    {
        var (p, renting, _) = Crear();
        renting.LoginFalla = true;
        var r = await p.ConsultarAsync("LZO227", Fecha, TestContext.Current.CancellationToken);
        (r.Resultado, r.TipoError, r.HttpStatus).Should().Be((ResultadoContratoConductor.Error, TipoErrorFuente.Autenticacion, 401));
        renting.Pedidos.Should().ContainSingle("sin token no se consulta");
    }

    [Fact]
    public void AC4_SinConfiguracion_QuedaNoConectado_ConLasVariablesQueFaltan()
    {
        var vacia = RentingContratosOpciones.Desde(new ConfigurationBuilder().Build());
        var p = new ProveedorContratosRenting(null!, vacia, null, TimeProvider.System);
        p.NoConectado.Should().StartWith("Falta configurar la API de contratos de Renting: RENTING_API_BASE_URL, RENTING_API_KEY_NAME")
            .And.Contain("RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL", "sin interruptor de contacto real, el desvío es obligatorio");

        var conReal = RentingContratosOpciones.Desde(new ConfigurationBuilder().AddInMemoryCollection(Config(contactoReal: true))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RENTING_API_CONTRATOS_DEVELOPMENT_EMAIL"] = null }).Build());
        conReal.Faltan().Should().BeEmpty("con el contacto real encendido no hace falta buzón de desvío");
    }

    [Fact]
    public void AC4_CertificadoQueNoExiste_DiceLaRuta_YNoLaClave()
    {
        var o = RentingContratosOpciones.Desde(new ConfigurationBuilder().AddInMemoryCollection(Config()).Build());
        var (certificado, motivo) = o.AbrirCertificado();
        certificado.Should().BeNull();
        motivo.Should().Contain("/no/se/abre/en/estas/pruebas.pfx");
    }

    [Fact]
    public void AC5_LaRespuestaDelContratoNoSeDevuelveCruda()
    {
        typeof(ConsultarContratoConductorResponse).GetProperties().Select(x => x.Name).Should()
            .NotContain(n => n.Contains("Cruda", StringComparison.OrdinalIgnoreCase), "el contrato trae datos personales de clientes reales");
    }

    [Fact]
    public async Task HU13843_Probar_SoloHaceLogin_ConTokenNuevo_YNoConsultaPlacas()
    {
        var (p, renting, _) = Crear();
        await p.ConsultarAsync("LZO227", Fecha, TestContext.Current.CancellationToken);
        var r = await p.ProbarAsync(TestContext.Current.CancellationToken);

        (r.Conectado, r.Configurado, r.HasMotivo, r.ContactoDesviado, r.CorreoDesvio).Should().Be((true, true, false, true, "buzon.pruebas@flitsas.com"));
        renting.Logins.Should().Be(2, "la prueba descarta el token guardado y vuelve a iniciar sesión");
        renting.Pedidos.Skip(2).Should().ContainSingle().Which.Url.Should().Contain("TokenForThird", "probar no consulta ninguna placa");
    }

    [Fact]
    public async Task HU13843_Probar_LoginRechazado_DiceElCodigo_SinLaLlave()
    {
        var (p, renting, _) = Crear();
        renting.LoginFalla = true;
        var r = await p.ProbarAsync(TestContext.Current.CancellationToken);
        (r.Conectado, r.Configurado, r.HttpStatus).Should().Be((false, true, 401));
        r.Motivo.Should().Contain("401").And.NotContain(Llave);
    }

    [Fact]
    public async Task HU13843_Probar_SinConfiguracion_NoLlamaARenting_YDiceQueFalta()
    {
        var vacia = RentingContratosOpciones.Desde(new ConfigurationBuilder().Build());
        var r = await new ProveedorContratosRenting(null!, vacia, null, TimeProvider.System).ProbarAsync(TestContext.Current.CancellationToken);
        (r.Conectado, r.Configurado, r.DuracionMs).Should().Be((false, false, 0));
        r.Motivo.Should().StartWith("Falta configurar la API de contratos de Renting: RENTING_API_BASE_URL");
    }

    [Fact]
    public async Task HU13843_Probar_ConContactoReal_NoDiceBuzon()
    {
        var (p, _, _) = Crear(contactoReal: true);
        var r = await p.ProbarAsync(TestContext.Current.CancellationToken);
        (r.Conectado, r.ContactoDesviado, r.HasCorreoDesvio).Should().Be((true, false, false));
    }

    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Advance(TimeSpan t) => _ahora += t;
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Renting falsa: la placa decide la respuesta de la consulta; el login entrega token-1, token-2, …</summary>
    internal sealed class RentingFalsa : HttpMessageHandler
    {
        private int _tokens;
        private bool _rechazoHecho;

        public bool LoginFalla { get; set; }

        public int Logins { get; private set; }

        public List<(string Url, string? Llave, string? Bearer, string? Cuerpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var llave = request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var v) ? v.Single() : null;
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((url, llave, request.Headers.Authorization?.Parameter, cuerpo));
            static HttpResponseMessage R(HttpStatusCode s, string body, string tipo = "application/json") => new(s) { Content = new StringContent(body, Encoding.UTF8, tipo) };

            if (url.Contains("TokenForThird", StringComparison.Ordinal))
            {
                Logins++;
                return LoginFalla ? R(HttpStatusCode.Unauthorized, "{}") : R(HttpStatusCode.OK, $$"""{"token":"token-{{++_tokens}}","refresh":"r"}""");
            }

            var placa = request.RequestUri.Query.Split("plate=")[1][..6];
            if (placa == "TOK401" && !_rechazoHecho)
            {
                _rechazoHecho = true;
                return R(HttpStatusCode.Unauthorized, "{}");
            }

            const string Contrato = """
                [{"names":"Ana Maria","firstSurname":"Perez","secondSurname":"Gomez","documentType":"cc","documentNumber":1020304050,
                  "customerEmail":"ana.perez@cliente.test","customerPhone":"3001112233","city":"MEDELLIN"}]
                """;
            return placa switch
            {
                "ARR000" => R(HttpStatusCode.OK, "[]"),
                "NOF404" => R(HttpStatusCode.NotFound, "{}"),
                "NUL000" => R(HttpStatusCode.OK, "null"),
                "SIN000" => R(HttpStatusCode.OK, """{"plate":"SIN000"}"""),
                "ERR500" => R(HttpStatusCode.InternalServerError, $"caído {Llave}"),
                "ERR403" => R(HttpStatusCode.Forbidden, "{}"),
                "HTM200" => R(HttpStatusCode.OK, "<html>mantenimiento</html>", "text/html"),
                "NUM200" => R(HttpStatusCode.OK, "42"),
                "RED000" => throw new HttpRequestException("conexión rechazada"),
                _ => R(HttpStatusCode.OK, Contrato),
            };
        }
    }
}
