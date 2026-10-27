using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Infrastructure.Consultations;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.Consultations;

/// <summary>
/// Resolvedor de errores del Diagnóstico — cada proveedor HTTP dice POR QUÉ no respondió (<see cref="ConsultationResult.Fallo"/>)
/// sin cambiar lo que ve Trámites: <c>Overall</c>, los checks y su mensaje son los de siempre para 401, 403, 429, 500, timeout y
/// red, y <c>Fallo</c> no se serializa. Datos ficticios (TST###, 99999…).
/// <para>Uso de ejemplo: <c>(await Simit(Http(HttpStatusCode.Forbidden)).ConsultAsync(CtxPlaca(), ct)).Fallo!.Clase</c> →
/// <see cref="ClaseFalloProveedor.Credenciales"/>.</para>
/// </summary>
public sealed class FalloProveedorHttpTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string MensajeSimit =
        "No fue posible verificar la información en SIMIT en este momento. Vuelve a intentarlo en unos minutos.";

    public static TheoryData<string, ClaseFalloProveedor, int?> Casos => new()
    {
        { "401", ClaseFalloProveedor.Credenciales, 401 },
        { "403", ClaseFalloProveedor.Credenciales, 403 },
        { "429", ClaseFalloProveedor.Limite, 429 },
        { "400", ClaseFalloProveedor.Rechazo, 400 },
        { "422", ClaseFalloProveedor.Rechazo, 422 },
        { "500", ClaseFalloProveedor.Caida, 500 },
        { "503", ClaseFalloProveedor.Caida, 503 },
        { "200-ilegible", ClaseFalloProveedor.Caida, 200 },
        { "timeout", ClaseFalloProveedor.Timeout, null },
        { "red", ClaseFalloProveedor.Red, null },
    };

    public static TheoryData<string, string, ClaseFalloProveedor, int?> CasosPorProveedor
    {
        get
        {
            var datos = new TheoryData<string, string, ClaseFalloProveedor, int?>();
            foreach (var proveedor in new[] { "verifik_simit", "verifik_rues", "flit_fines", "kyverum_fines", "verifik_conductor" })
            {
                foreach (var (caso, clase, status) in new (string, ClaseFalloProveedor, int?)[]
                {
                    ("401", ClaseFalloProveedor.Credenciales, 401), ("403", ClaseFalloProveedor.Credenciales, 403),
                    ("429", ClaseFalloProveedor.Limite, 429), ("500", ClaseFalloProveedor.Caida, 500),
                    ("timeout", ClaseFalloProveedor.Timeout, null),
                })
                {
                    datos.Add(proveedor, caso, clase, status);
                }
            }

            return datos;
        }
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task Verifik_SIMIT_clasifica_el_fallo_y_conserva_Overall_checks_y_mensaje(string caso, ClaseFalloProveedor clase, int? status)
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Simit(Handler(caso)).ConsultAsync(CtxPlaca(), ct);

        result.Overall.Should().Be("red", caso);
        var check = result.Checks.Should().ContainSingle(caso).Subject;
        (check.Key, check.Label, check.Status, check.Source, check.Message)
            .Should().Be(("provider", "Consulta SIMIT", "error", "verifik_simit", MensajeSimit), caso);
        result.HydratedFields.Should().BeEmpty(caso);
        result.Fallo.Should().Be(new FalloProveedor(clase, status), caso);
    }

    [Theory]
    [MemberData(nameof(CasosPorProveedor))]
    public async Task Cada_proveedor_HTTP_clasifica_el_fallo_sin_cambiar_lo_que_responde(
        string proveedor, string caso, ClaseFalloProveedor clase, int? status)
    {
        var ct = TestContext.Current.CancellationToken;
        var (conFallo, ctx) = Construir(proveedor, Handler(caso));
        var (referencia, _) = Construir(proveedor, Handler("503"));

        var result = await conFallo.ConsultAsync(ctx, ct);
        var hoy = await referencia.ConsultAsync(ctx, ct);

        // Hoy todo no-2xx (salvo 404), timeout y red responde lo mismo: el 503 es la referencia de «lo de siempre».
        result.Overall.Should().Be(hoy.Overall, $"{proveedor} {caso}");
        result.Checks.Should().BeEquivalentTo(hoy.Checks, o => o.WithStrictOrdering(), $"{proveedor} {caso}");
        result.Checks.Should().Contain(c => c.Status == "error", $"{proveedor} {caso}");
        result.Fallo.Should().Be(new FalloProveedor(clase, status), $"{proveedor} {caso}");
    }

    [Fact]
    public async Task El_fallo_no_se_serializa_lo_que_guarda_Tramites_no_cambia()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Simit(Handler("403")).ConsultAsync(CtxPlaca(), ct);

        result.Fallo.Should().NotBeNull();
        var json = JsonSerializer.Serialize(result, Web);
        json.Should().NotContain("fallo", "Fallo es solo para core-consultas; el snapshot y la caché de Trámites no cambian");
        json.Should().Be(JsonSerializer.Serialize(result with { Fallo = null }, Web));
    }

    [Fact]
    public async Task Respuesta_exitosa_y_404_no_llevan_fallo()
    {
        var ct = TestContext.Current.CancellationToken;

        var noEncontrado = await Simit(new Respuesta(HttpStatusCode.NotFound, "{}")).ConsultAsync(CtxPlaca(), ct);

        noEncontrado.Fallo.Should().BeNull();
        noEncontrado.Overall.Should().Be("green");
    }

    [Fact]
    public void El_codigo_del_fallo_es_el_del_sobre_del_bus()
    {
        new FalloProveedor(ClaseFalloProveedor.Credenciales, 403).Codigo.Should().Be("CREDENCIALES");
        new FalloProveedor(ClaseFalloProveedor.Limite, 429).Codigo.Should().Be("LIMITE");
        new FalloProveedor(ClaseFalloProveedor.Rechazo, 400).Codigo.Should().Be("RECHAZO");
        new FalloProveedor(ClaseFalloProveedor.Caida, 500).Codigo.Should().Be("CAIDA");
        new FalloProveedor(ClaseFalloProveedor.Red).Codigo.Should().Be("RED");
        new FalloProveedor(ClaseFalloProveedor.Timeout).Codigo.Should().Be("TIMEOUT");
    }

    private static HttpMessageHandler Handler(string caso) => caso switch
    {
        "timeout" => new Lanza(new TaskCanceledException("timeout del HttpClient")),
        "red" => new Lanza(new HttpRequestException("conexión rechazada")),
        "200-ilegible" => new Respuesta(HttpStatusCode.OK, "<html>no es json</html>"),
        _ => new Respuesta((HttpStatusCode)int.Parse(caso, System.Globalization.CultureInfo.InvariantCulture),
            """{"message":"token_expired","code":"FORBIDDEN"}"""),
    };

    private static HttpClient Http(HttpMessageHandler handler, string baseUrl = "https://api.prueba") =>
        new(handler) { BaseAddress = new Uri(baseUrl) };

    private static VerifikSimitConsultationProvider Simit(HttpMessageHandler handler) =>
        new(Http(handler), Options.Create(new VerifikOptions { ApiToken = "t", AuthScheme = "Bearer" }),
            Options.Create(new ConsultationProviderModeOptions { VerifikSimitMode = "real" }));

    private static (IConsultationProvider Proveedor, ConsultationContext Ctx) Construir(string proveedor, HttpMessageHandler handler)
    {
        var verifik = Options.Create(new VerifikOptions { ApiToken = "t", AuthScheme = "Bearer" });
        return proveedor switch
        {
            "verifik_simit" => (Simit(handler), CtxPlaca()),
            "verifik_rues" => (new VerifikRuesConsultationProvider(Http(handler), verifik,
                Options.Create(new ConsultationProviderModeOptions { VerifikRuesMode = "real" })), Ctx(("nit", "900000001"))),
            "verifik_conductor" => (new VerifikConductorConsultationProvider(Http(handler), verifik,
                Options.Create(new ConsultationProviderModeOptions { VerifikConductorMode = "real" })),
                Ctx(("document_type", "CC"), ("document_number", "9999900001"))),
            "flit_fines" => (FlitFines(handler), Ctx(("owner_document_type", "CC"), ("owner_document_number", "9999900001"))),
            "kyverum_fines" => (KyverumFines(handler), Ctx(("owner_document_type", "NIT"), ("owner_document_number", "900000001"))),
            _ => throw new ArgumentOutOfRangeException(nameof(proveedor)),
        };
    }

    private static FlitFinesConsultationProvider FlitFines(HttpMessageHandler handler)
    {
        var opts = new FlitRegistrationApiOptions { BaseUrl = "https://api.prueba/pdn" };
        return new FlitFinesConsultationProvider(Http(handler, opts.NormalizedBaseUrl), Options.Create(opts),
            Options.Create(new ConsultationProviderModeOptions { FlitFinesMode = "real" }));
    }

    private static KyverumFinesConsultationProvider KyverumFines(HttpMessageHandler handler)
    {
        var opts = new KyverumFinesOptions
        {
            BaseUrl = "https://runt.prueba",
            InfractionPath = "/v1/comparendos:consultar",
            ApiKey = "k",
            AuthScheme = "Bearer",
        };
        return new KyverumFinesConsultationProvider(Http(handler, opts.BaseUrl), Options.Create(opts),
            Options.Create(new ConsultationProviderModeOptions { KyverumFinesMode = "real" }));
    }

    private static ConsultationContext CtxPlaca() => Ctx(("plate", "TST901"));

    private static ConsultationContext Ctx(params (string Clave, string Valor)[] campos)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valor) in campos)
            dict[clave] = valor;
        return new ConsultationContext(Guid.Empty, Guid.Empty, "prueba", dict);
    }

    private sealed class Respuesta(HttpStatusCode status, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
    }

    private sealed class Lanza(Exception excepcion) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(excepcion);
    }
}
