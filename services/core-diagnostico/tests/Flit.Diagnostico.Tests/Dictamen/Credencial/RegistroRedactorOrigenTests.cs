using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Diagnostico.Tests.Dictamen.Credencial;

/// <summary>
/// ADR-0091 §3.1, SP-5, SP-9, SP-13, SP-16: <c>Diagnostico:Ia:Origen</c>. <c>local</c> (por defecto) deja el comportamiento de siempre;
/// <c>plataforma</c> nunca simula, toma la llave de core-api por gRPC con el scope único <c>platform.ia.diagnostico</c> y, si le falta
/// la dirección o una privada RSA-3072 válida, queda sin configurar (plantilla) con un aviso de arranque sin valores.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~RegistroRedactorOrigenTests"
/// </code>
/// </example>
public sealed class RegistroRedactorOrigenTests : IDisposable
{
    private static readonly VectorIa V = VectorIa.Cargar();
    private readonly string _dir = Directory.CreateTempSubdirectory("flit-ia-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Archivo(string nombre, string contenido)
    {
        var ruta = Path.Combine(_dir, nombre);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    private Dictionary<string, string?> Plataforma(Action<Dictionary<string, string?>>? ajuste = null)
    {
        var c = new Dictionary<string, string?>
        {
            ["Diagnostico:Ambiente"] = "DEV",
            ["Diagnostico:Anthropic:Simulado"] = "true",
            ["Diagnostico:Simulacion:Habilitada"] = "true",
            ["Diagnostico:Ia:Origen"] = "plataforma",
            ["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("privada.p8", V.PrivadaBase64 + "\n"),
            ["Diagnostico:Ia:Kid"] = V.Kid,
            ["Diagnostico:Tramites:Address"] = "http://core-api:8082",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
            ["Platform:ServiceClient:ClientId"] = "svc-diagnostico",
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        };
        ajuste?.Invoke(c);
        return c;
    }

    private static ServiceProvider Construir(Dictionary<string, string?> valores, LogsIa? logs = null, Action<IServiceCollection>? extra = null)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicios = new ServiceCollection();
        servicios.AddLogging(b =>
        {
            if (logs is not null)
                b.AddProvider(logs);
        });
        servicios.AddRedactorDictamen(configuracion);
        extra?.Invoke(servicios);
        return servicios.BuildServiceProvider();
    }

    [Fact]
    public void SP5_Plataforma_EsAnthropicConFuenteRemota_NuncaSimulado_AunEnDevConSimulacion()
    {
        using var sp = Construir(Plataforma());

        var proveedor = sp.GetRequiredService<IProveedorRedactor>();

        proveedor.Should().BeOfType<ProveedorRedactorAnthropic>();
        proveedor.Configurado.Should().BeTrue();
        sp.GetRequiredService<IFuenteCredencialRedactor>().Should().BeOfType<FuenteCredencialPlataforma>();
    }

    [Fact]
    public void Plataforma_SinDistinguirMayusculas()
    {
        using var sp = Construir(Plataforma(c => c["Diagnostico:Ia:Origen"] = "PLATAFORMA"));

        sp.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorAnthropic>();
    }

    [Fact]
    public void SP16_LaPrivadaEnPemPkcs8TambienSirve()
    {
        using var rsa = V.Privada();
        var pem = rsa.ExportPkcs8PrivateKeyPem();

        using var sp = Construir(Plataforma(c => c["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("privada.pem", pem)));

        sp.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorAnthropic>();
    }

    public static TheoryData<string> FaltasDePlataforma =>
        ["sin_direccion", "sin_kid", "kid_invalido", "sin_privada", "privada_inexistente", "privada_ilegible", "privada_2048", "pem_public_key", "pem_rsa_public_key"];

    [Theory]
    [MemberData(nameof(FaltasDePlataforma))]
    public void SP9_SP16_PlataformaIncompleta_QuedaSinConfigurar_SinSimular(string falta)
    {
        using var rsa2048 = RSA.Create(2048);
        var valores = Plataforma(c =>
        {
            switch (falta)
            {
                case "sin_direccion": c["Diagnostico:Tramites:Address"] = string.Empty; break;
                case "sin_kid": c["Diagnostico:Ia:Kid"] = string.Empty; break;
                case "kid_invalido": c["Diagnostico:Ia:Kid"] = "Diag_Prueba"; break;
                case "sin_privada": c["Diagnostico:Ia:ClavePrivadaArchivo"] = string.Empty; break;
                case "privada_inexistente": c["Diagnostico:Ia:ClavePrivadaArchivo"] = Path.Combine(_dir, "no-existe.p8"); break;
                case "privada_ilegible": c["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("mala.p8", "esto no es una clave"); break;
                case "pem_public_key": c["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("publica.pem", PublicaPem(spki: true)); break;
                case "pem_rsa_public_key": c["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("publica-rsa.pem", PublicaPem(spki: false)); break;
                default: c["Diagnostico:Ia:ClavePrivadaArchivo"] = Archivo("corta.p8", Convert.ToBase64String(rsa2048.ExportPkcs8PrivateKey())); break;
            }
        });

        using var sp = Construir(valores);

        sp.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorNoConfigurado>(falta);
    }

    private static string PublicaPem(bool spki)
    {
        using var publica = V.Publica();
        publica.KeySize.Should().BeGreaterThanOrEqualTo(ClavesPrivadasIa.BitsMinimos);
        return spki ? publica.ExportSubjectPublicKeyInfoPem() : publica.ExportRSAPublicKeyPem();
    }

    /// <summary>CR-4: una PEM pública (PUBLIC KEY o RSA PUBLIC KEY) de 3072 bits no pasa por privada: motivo PRIVADA_NO_ES_PRIVADA.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CR4_PemPublicaComoPrivada_EsPrivadaNoEsPrivada_YQuedaSinConfigurar(bool spki)
    {
        var archivo = Archivo(spki ? "publica.pem" : "publica-rsa.pem", PublicaPem(spki));
        var opciones = new OpcionesIa { Origen = OpcionesIa.OrigenPlataforma, Kid = V.Kid, ClavePrivadaArchivo = archivo };

        var claves = ClavesPrivadasIa.Cargar(opciones, out var motivo, out _);

        claves.Should().BeNull();
        motivo.Should().Be("PRIVADA_NO_ES_PRIVADA");

        var logs = new LogsIa();
        using var sp = Construir(Plataforma(c => c["Diagnostico:Ia:ClavePrivadaArchivo"] = archivo), logs);
        sp.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorNoConfigurado>();
        foreach (var h in sp.GetServices<IHostedService>())
            await h.StartAsync(TestContext.Current.CancellationToken);
        logs.Todo.Should().Contain("PRIVADA_NO_ES_PRIVADA").And.NotContain(_dir);
    }

    [Fact]
    public void OrigenDesconocido_QuedaSinConfigurar()
    {
        using var sp = Construir(Plataforma(c => c["Diagnostico:Ia:Origen"] = "remoto"));

        sp.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorNoConfigurado>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("local")]
    [InlineData("")]
    public void Local_SigueComoAntes_LlaveLocalOSimuladoEnDev(string? origen)
    {
        using var conLlave = Construir(new Dictionary<string, string?>
        {
            ["Diagnostico:Ambiente"] = "QA",
            ["Diagnostico:Ia:Origen"] = origen,
            ["Diagnostico:Anthropic:ApiKey"] = "llave-del-desarrollador",
        });
        using var simulado = Construir(new Dictionary<string, string?>
        {
            ["Diagnostico:Ambiente"] = "DEV",
            ["Diagnostico:Ia:Origen"] = origen,
            ["Diagnostico:Simulacion:Habilitada"] = "true",
        });

        conLlave.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorAnthropic>();
        conLlave.GetRequiredService<IFuenteCredencialRedactor>().Should().BeOfType<FuenteCredencialLocal>();
        simulado.GetRequiredService<IProveedorRedactor>().Should().BeOfType<ProveedorRedactorSimulado>();
    }

    [Fact]
    public async Task SP13_ElClienteGrpcPideElTokenConElScopeUnicoPlatformIaDiagnostico()
    {
        using var publica = V.Publica();
        await using var core = await CoreApiIaFalso.IniciarAsync();
        core.Responder = p => Task.FromResult(CoreApiIaFalso.Valida(p, publica, DateTimeOffset.UtcNow));
        var identidad = new Identidad();
        using var sp = Construir(Plataforma(), extra: s =>
        {
            s.AddHttpClient(ServiceTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => identidad);
            s.PostConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b =>
            {
                if (b.Name != ServiceTokenProvider.HttpClientName)
                    b.PrimaryHandler = core.Handler;
            }));
        });

        var r = await sp.GetRequiredService<IFuenteCredencialRedactor>().ObtenerAsync(TestContext.Current.CancellationToken);

        r.Falla.Should().Be(FallaCredencial.Ninguna);
        r.Credencial!.ApiKey.Should().Be("sk-ant-test-CENTINELA");
        ConfiguracionIaPlataforma.Scope.Should().Be("platform.ia.diagnostico");
        identidad.Scopes.Should().Equal("platform.ia.diagnostico");
        core.Pedidos.Single().ToolCode.Should().Be("diagnostico_dictamen");
    }

    [Fact]
    public async Task SP16_ElLogDeArranqueDiceOrigenProveedorYKid_SinLaPrivada()
    {
        var logs = new LogsIa();
        using var sp = Construir(Plataforma(), logs);

        foreach (var h in sp.GetServices<IHostedService>())
            await h.StartAsync(TestContext.Current.CancellationToken);

        logs.Todo.Should().Contain("plataforma").And.Contain("anthropic").And.Contain(V.Kid).And.Contain("presente");
        logs.Todo.Should().NotContain(V.PrivadaBase64[..40]).And.NotContain("simulado");
    }

    [Fact]
    public async Task PlataformaIncompleta_AvisaAlArrancarSinValores()
    {
        var logs = new LogsIa();
        using var sp = Construir(Plataforma(c => c["Diagnostico:Tramites:Address"] = string.Empty), logs);

        foreach (var h in sp.GetServices<IHostedService>())
            await h.StartAsync(TestContext.Current.CancellationToken);

        logs.Eventos.Should().Contain(e => e.Nivel == LogLevel.Warning && e.Texto.Contains("sin configuración de plataforma", StringComparison.Ordinal));
        logs.Todo.Should().NotContain(V.PrivadaBase64[..40]).And.NotContain(_dir);
    }

    private sealed class Identidad : HttpMessageHandler
    {
        public List<string> Scopes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var scope = form.Split('&').Select(WebUtility.UrlDecode).Single(p => p!.StartsWith("scope=", StringComparison.Ordinal))!["scope=".Length..];
            Scopes.Add(scope);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { access_token = "token-de-prueba", expires_in = 300 }), Encoding.UTF8, "application/json"),
            };
        }
    }
}
