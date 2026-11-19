using System.Security.Cryptography;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Entrega;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai.Entrega;

/// <summary>
/// ADR-0091 §4 / contrato §1.3 — entrega de la configuración de <c>diagnostico_dictamen</c>: cola enabled ∩ capaz ∩ con llave
/// (solo anthropic), modelo por herramienta, TTL acotado a 300 s, sin fila / sin llave ⇒ <c>disponible=false</c> con su motivo,
/// claves públicas por <c>kid</c> (actual y siguiente, RSA ≥ 3072).
/// </summary>
/// <remarks>Uso de ejemplo: <c>await servicio.EntregarAsync(new SolicitudEntregaIa("diagnostico", "diagnostico_dictamen", kid, nonce), ct)</c>.</remarks>
public sealed class ServicioEntregaConfiguracionIaTests
{
    private const string Tool = AiToolCodes.DiagnosticoDictamen;
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 12, 0, 0, 750, TimeSpan.Zero);

    private readonly IAiToolProviderSettingsRepository _repo = Substitute.For<IAiToolProviderSettingsRepository>();
    private readonly IAiApiKeyEnvironmentProbe _probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
    private readonly LectorFalso _lector = new();
    private readonly LoggerCapturado _logRegistro = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static AiToolProviderSettings FilaSembrada(bool openAiHabilitado = false) => new(
        Guid.CreateVersion7(), Tool, AiProviderCodes.Anthropic, AutoReturnToPrimary: true,
        [
            new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
            new AiProviderSlot(AiProviderCodes.OpenAi, openAiHabilitado, 2),
            new AiProviderSlot(AiProviderCodes.Kimi, false, 3),
            new AiProviderSlot(AiProviderCodes.DeepSeek, false, 4),
        ],
        RowVersion: 7, CreatedAt: Ahora.AddDays(-1), UpdatedAt: null, UpdatedBy: null);

    private ServicioEntregaConfiguracionIa Servicio(Dictionary<string, string?>? extra = null, LoggerCapturado? logEntrega = null)
    {
        var valores = new Dictionary<string, string?>
        {
            ["Ai:Entrega:Diagnostico:Kid"] = SelladorLlaveIaTests.Kid,
            ["Ai:Entrega:Diagnostico:ClavePublica"] = SelladorLlaveIaTests.PublicaBase64,
        };
        foreach (var (k, v) in extra ?? [])
            valores[k] = v;
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        // ActivatorUtilities: el logger del servicio sale del contenedor (parámetro opcional), igual que en DI.
        var servicios = new ServiceCollection().AddSingleton<ILogger<ServicioEntregaConfiguracionIa>>(logEntrega ?? new LoggerCapturado()).BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<ServicioEntregaConfiguracionIa>(servicios,
            _repo, _probe, _lector, new RegistroClavesDestinatarioIa(config, _logRegistro), config, (TimeProvider)new RelojFijo(Ahora));
    }

    private void ConLlaves(params string[] proveedores)
    {
        foreach (var p in proveedores)
        {
            _probe.HasApiKey(p).Returns(true);
            _lector.Llaves[p] = SelladorLlaveIaTests.Llave + "-" + p;
        }
    }

    private static SolicitudEntregaIa Solicitud() => new("diagnostico", Tool, SelladorLlaveIaTests.Kid, SelladorLlaveIaTests.Nonce);

    [Fact]
    public async Task ConFilaYLlave_EntregaSoloAnthropic_ConModeloPorDefecto_YLlaveSelladaConElAadDelModelo()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(FilaSembrada(openAiHabilitado: true));
        ConLlaves(AiProviderCodes.Anthropic, AiProviderCodes.OpenAi);

        var r = await Servicio().EntregarAsync(Solicitud(), Ct);

        r.Disponible.Should().BeTrue();
        r.Motivo.Should().Be(MotivoNoDisponibleIa.Ninguno);
        r.ToolCode.Should().Be(Tool);
        r.ProveedorActivo.Should().Be(AiProviderCodes.Anthropic);
        r.RetornoAutomatico.Should().BeTrue();
        r.RowVersion.Should().Be(7);
        r.TtlSegundos.Should().Be(300);
        r.EmitidaEn.Should().Be(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero), "segundos enteros: los nanos no entran al AAD");
        r.Cola.Should().ContainSingle("openai está habilitado y con llave, pero no es capaz para diagnostico_dictamen");
        var p = r.Cola[0];
        p.Proveedor.Should().Be(AiProviderCodes.Anthropic);
        p.Prioridad.Should().Be(1);
        p.Modelo.Should().Be("claude-haiku-4-5");
        p.Llave.Kid.Should().Be(SelladorLlaveIaTests.Kid);
        var aad = AadLlaveIa.Componer("diagnostico", Tool, p.Proveedor, p.Modelo, r.RowVersion, SelladorLlaveIaTests.Kid, SelladorLlaveIaTests.Nonce, r.EmitidaEn.ToUnixTimeSeconds());
        SelladorLlaveIaTests.Descifrar(SelladorLlaveIaTests.Privada, p.Llave, aad).Should().Be(SelladorLlaveIaTests.Llave + "-anthropic");
    }

    [Fact]
    public async Task ElModelo_SaleDeAiHerramientas()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(FilaSembrada());
        ConLlaves(AiProviderCodes.Anthropic);

        var r = await Servicio(new() { ["Ai:Herramientas:diagnostico_dictamen:Modelos:anthropic"] = "claude-sonnet-4-5" }).EntregarAsync(Solicitud(), Ct);

        r.Cola.Single().Modelo.Should().Be("claude-sonnet-4-5");
    }

    [Fact]
    public async Task SinLlave_ElProveedorSaleDeLaCola_YColaVaciaEsNoDisponibleSinProveedorConLlave()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(FilaSembrada());
        _probe.HasApiKey(Arg.Any<string>()).Returns(false);

        var r = await Servicio().EntregarAsync(Solicitud(), Ct);

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoNoDisponibleIa.SinProveedorConLlave);
        r.Cola.Should().BeEmpty();
        r.TtlSegundos.Should().Be(60);
        r.RowVersion.Should().Be(7);
        _lector.Lecturas.Should().Be(0, "sin llave no se lee nada para sellar");
    }

    [Fact]
    public async Task SinFila_EsNoDisponibleHerramientaSinConfigurar()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns((AiToolProviderSettings?)null);

        var r = await Servicio().EntregarAsync(Solicitud(), Ct);

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoNoDisponibleIa.HerramientaSinConfigurar);
        r.Cola.Should().BeEmpty();
        r.TtlSegundos.Should().Be(60);
    }

    [Theory]
    [InlineData("86400", 300)]
    [InlineData("120", 120)]
    [InlineData("0", 300)]
    [InlineData("-5", 300)]
    public async Task ElTtl_NuncaPasaDe300(string configurado, int esperado)
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(FilaSembrada());
        ConLlaves(AiProviderCodes.Anthropic);

        var r = await Servicio(new() { ["Ai:Entrega:TtlSegundos"] = configurado }).EntregarAsync(Solicitud(), Ct);

        r.TtlSegundos.Should().Be(esperado);
    }

    [Fact]
    public void ConoceKid_ActualYSiguiente_NoOtro_NiOtraAudiencia()
    {
        using var otra = RSA.Create(3072);
        var servicio = Servicio(new()
        {
            ["Ai:Entrega:Diagnostico:KidSiguiente"] = "diag-dev-202612",
            ["Ai:Entrega:Diagnostico:ClavePublicaSiguiente"] = Convert.ToBase64String(otra.ExportSubjectPublicKeyInfo()),
        });

        servicio.ConoceKid("diagnostico", SelladorLlaveIaTests.Kid).Should().BeTrue();
        servicio.ConoceKid("diagnostico", "diag-dev-202612").Should().BeTrue();
        servicio.ConoceKid("diagnostico", "diag-dev-209901").Should().BeFalse();
        servicio.ConoceKid("comparendos", SelladorLlaveIaTests.Kid).Should().BeFalse();
    }

    [Fact]
    public void ClavePublicaDe2048_OInvalida_SeDescarta_ConWarningSinElValor()
    {
        using var corta = RSA.Create(2048);
        var b64Corta = Convert.ToBase64String(corta.ExportSubjectPublicKeyInfo());
        var servicio = Servicio(new()
        {
            ["Ai:Entrega:Diagnostico:ClavePublica"] = b64Corta,
            ["Ai:Entrega:Diagnostico:KidSiguiente"] = "diag-dev-202612",
            ["Ai:Entrega:Diagnostico:ClavePublicaSiguiente"] = "no-es-base64!!",
        });

        servicio.ConoceKid("diagnostico", SelladorLlaveIaTests.Kid).Should().BeFalse("SP-9: RSA < 3072 se rechaza");
        servicio.ConoceKid("diagnostico", "diag-dev-202612").Should().BeFalse();
        _logRegistro.Entradas.Should().Contain(e => e.Nivel == LogLevel.Warning);
        var prefijo = b64Corta.Substring(0, 40);
        _logRegistro.Entradas.Should().NotContain(e => e.Mensaje.Contains(prefijo, StringComparison.Ordinal) || e.Mensaje.Contains("no-es-base64", StringComparison.Ordinal));
    }

    [Fact]
    public void LaClavePublicaAceptaPem()
    {
        var pem = SelladorLlaveIaTests.Privada.ExportSubjectPublicKeyInfoPem();

        Servicio(new() { ["Ai:Entrega:Diagnostico:ClavePublica"] = pem }).ConoceKid("diagnostico", SelladorLlaveIaTests.Kid).Should().BeTrue();
    }

    /// <summary>
    /// Review HU-6 · CR-3 — PEM solo con etiqueta <c>PUBLIC KEY</c>: una privada (PKCS#8 o PKCS#1) o un <c>RSA PUBLIC KEY</c>
    /// se descartan con motivo <c>no_es_publica</c> y un Warning que no lleva el contenido.
    /// </summary>
    [Theory]
    [InlineData("PRIVATE KEY")]
    [InlineData("RSA PRIVATE KEY")]
    [InlineData("RSA PUBLIC KEY")]
    public void CR3_PemQueNoEsPublicKey_SeDescarta_NoEsPublica_SinElContenido(string etiqueta)
    {
        using var par = RSA.Create(3072);
        var pem = etiqueta switch
        {
            "PRIVATE KEY" => par.ExportPkcs8PrivateKeyPem(),
            "RSA PRIVATE KEY" => par.ExportRSAPrivateKeyPem(),
            _ => par.ExportRSAPublicKeyPem(),
        };
        pem.Should().StartWith($"-----BEGIN {etiqueta}-----");
        var cuerpo = pem.Split('\n')[1].Trim();

        var servicio = Servicio(new() { ["Ai:Entrega:Diagnostico:ClavePublica"] = pem });

        servicio.ConoceKid("diagnostico", SelladorLlaveIaTests.Kid).Should().BeFalse();
        _logRegistro.Entradas.Should().Contain(e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains("motivo=no_es_publica", StringComparison.Ordinal));
        _logRegistro.Entradas.Should().NotContain(e => e.Mensaje.Contains(cuerpo, StringComparison.Ordinal) || e.Mensaje.Contains("BEGIN", StringComparison.Ordinal));
    }

    /// <summary>Review HU-6 · CR-3 — la base64 de DER SPKI en una línea sigue valiendo.</summary>
    [Fact]
    public void CR3_Base64DerSpki_SigueAceptado() =>
        Servicio().ConoceKid("diagnostico", SelladorLlaveIaTests.Kid).Should().BeTrue();

    public static TheoryData<string> ModelosInvalidos => [".x", "-x", "a\n", "a" + new string('b', 128)];

    /// <summary>
    /// Review HU-6 · CR-4 — modelo con la regex del cliente (<c>^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\z</c>): si el configurado no
    /// la cumple, el proveedor sale de la cola; con la cola vacía ⇒ <c>disponible=false</c> / <c>SIN_PROVEEDOR_CON_LLAVE</c>,
    /// Warning sin el valor y nunca una excepción (INTERNAL).
    /// </summary>
    [Theory]
    [MemberData(nameof(ModelosInvalidos))]
    public async Task CR4_ModeloInvalido_SacaAlProveedor_NoDisponibleSinProveedorConLlave_ConWarningSinElValor(string modelo)
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(FilaSembrada());
        ConLlaves(AiProviderCodes.Anthropic);
        var logEntrega = new LoggerCapturado();

        var r = await Servicio(new() { ["Ai:Herramientas:diagnostico_dictamen:Modelos:anthropic"] = modelo }, logEntrega).EntregarAsync(Solicitud(), Ct);

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoNoDisponibleIa.SinProveedorConLlave);
        r.Cola.Should().BeEmpty();
        logEntrega.Entradas.Should().Contain(e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains("proveedor=anthropic", StringComparison.Ordinal));
        logEntrega.Entradas.Should().NotContain(e => e.Mensaje.Contains(modelo, StringComparison.Ordinal));
        _lector.Lecturas.Should().Be(0, "con el modelo inválido no se lee la llave");
    }

    internal sealed class LectorFalso : ILectorLlaveProveedorIa
    {
        public Dictionary<string, string> Llaves { get; } = [];

        public int Lecturas { get; private set; }

        public string? LeerLlave(string providerCode)
        {
            Lecturas++;
            return Llaves.GetValueOrDefault(providerCode);
        }
    }

    internal sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    internal sealed class LoggerCapturado : ILogger<RegistroClavesDestinatarioIa>, ILogger<ServicioEntregaConfiguracionIa>
    {
        public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
    }
}
