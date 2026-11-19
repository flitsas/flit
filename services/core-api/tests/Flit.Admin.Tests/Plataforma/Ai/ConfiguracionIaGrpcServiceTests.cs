using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Api.Grpc;
using Flit.Infrastructure.Ai.Entrega;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

/// <summary>
/// ADR-0091 · contrato §1–§1.3 · SP-15/19/20 — <c>ConfiguracionIaService.ObtenerConfiguracionIa</c>: solo svc-diagnostico y solo
/// <c>diagnostico_dictamen</c>; validación de tool/nonce/kid; kid desconocido ⇒ FAILED_PRECONDITION; sin llave ⇒ OK con
/// <c>disponible=false</c>; errores internos sin detalle; auditoría por log sin llave ni sobre.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await servicio.ObtenerConfiguracionIa(new ObtenerConfiguracionIaRequest { ToolCode = "diagnostico_dictamen", Kid = kid, Nonce = nonce }, contexto)</c>.</remarks>
public sealed class ConfiguracionIaGrpcServiceTests
{
    private const string Tool = "diagnostico_dictamen";
    private const string Kid = "diag-dev-202611";
    private const string Centinela = "sk-ant-test-CENTINELA-0123456789";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Lazy<RSA> Par = new(() => RSA.Create(3072));

    private readonly IAiToolProviderSettingsRepository _repo = Substitute.For<IAiToolProviderSettingsRepository>();
    private readonly IAiApiKeyEnvironmentProbe _probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
    private readonly LoggerCapturado _logger = new();
    private readonly Lector _lector = new();

    public ConfiguracionIaGrpcServiceTests()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns(new AiToolProviderSettings(
            Guid.CreateVersion7(), Tool, AiProviderCodes.Anthropic, true,
            [
                new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
                new AiProviderSlot(AiProviderCodes.OpenAi, false, 2),
                new AiProviderSlot(AiProviderCodes.Kimi, false, 3),
                new AiProviderSlot(AiProviderCodes.DeepSeek, false, 4),
            ],
            RowVersion: 7, CreatedAt: Ahora, UpdatedAt: null, UpdatedBy: null));
        _probe.HasApiKey(AiProviderCodes.Anthropic).Returns(true);
        _lector.Llave = Centinela;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ByteString Nonce(int n = 16) => ByteString.CopyFrom(Enumerable.Range(1, n).Select(i => (byte)i).ToArray());

    private IEntregaConfiguracionIa EntregaReal()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Entrega:Diagnostico:Kid"] = Kid,
            ["Ai:Entrega:Diagnostico:ClavePublica"] = Convert.ToBase64String(Par.Value.ExportSubjectPublicKeyInfo()),
        }).Build();
        return new ServicioEntregaConfiguracionIa(_repo, _probe, _lector,
            new RegistroClavesDestinatarioIa(config, NullLogger<RegistroClavesDestinatarioIa>.Instance), config, new Reloj(Ahora));
    }

    private ConfiguracionIaGrpcService Servicio(IEntregaConfiguracionIa? entrega = null) => new(entrega ?? EntregaReal(), _logger);

    private static ObtenerConfiguracionIaRequest Request(string tool = Tool, string kid = Kid, ByteString? nonce = null) =>
        new() { ToolCode = tool, Kid = kid, Nonce = nonce ?? Nonce() };

    private static async Task<RpcException> Falla(Func<Task> act, StatusCode estado, string reason)
    {
        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(estado);
        PlatformRpcErrors.Reason(ex).Should().Be(reason);
        return ex;
    }

    [Fact]
    public async Task SvcDiagnostico_RecibeLaColaConLaLlaveSellada_YElEcoDeNonceYKid()
    {
        var r = await Servicio().ObtenerConfiguracionIa(Request(), new Contexto(Ct));

        r.ToolCode.Should().Be(Tool);
        r.Disponible.Should().BeTrue();
        r.Motivo.Should().Be(MotivoNoDisponible.Unspecified);
        r.ProveedorActivo.Should().Be("anthropic");
        r.RetornoAutomatico.Should().BeTrue();
        r.RowVersion.Should().Be(7);
        r.TtlSegundos.Should().Be(300);
        r.EmitidaEn.Seconds.Should().Be(Ahora.ToUnixTimeSeconds());
        r.EmitidaEn.Nanos.Should().Be(0);
        r.Nonce.Should().Equal(Nonce());
        r.Kid.Should().Be(Kid);
        var p = r.Cola.Should().ContainSingle().Which;
        p.Proveedor.Should().Be("anthropic");
        p.Prioridad.Should().Be(1);
        p.Modelo.Should().Be("claude-haiku-4-5");
        p.Llave.Alg.Should().Be("RSA-OAEP-256+A256GCM");
        p.Llave.Kid.Should().Be(Kid);
        p.Llave.CekCifrada.Length.Should().Be(384);
        p.Llave.Iv.Length.Should().Be(12);
        p.Llave.Tag.Length.Should().Be(16);

        // Lo que hará Diagnóstico: recomponer el AAD con sus valores y descifrar con la privada.
        var aad = Encoding.UTF8.GetBytes($"flit.ia.llave.v1\ndiagnostico\n{Tool}\nanthropic\nclaude-haiku-4-5\n7\n{Kid}\nAQIDBAUGBwgJCgsMDQ4PEA\n{r.EmitidaEn.Seconds}");
        var cek = Par.Value.Decrypt(p.Llave.CekCifrada.ToByteArray(), RSAEncryptionPadding.OaepSHA256);
        var plano = new byte[p.Llave.TextoCifrado.Length];
        using (var aes = new AesGcm(cek, 16))
            aes.Decrypt(p.Llave.Iv.Span, p.Llave.TextoCifrado.Span, p.Llave.Tag.Span, plano, aad);
        Encoding.UTF8.GetString(plano).Should().Be(Centinela);

        r.ToString().Should().NotContain("CENTINELA", "la respuesta nunca lleva la llave en claro");
    }

    [Theory]
    [InlineData("svc-ict")]
    [InlineData("svc-tramites")]
    public async Task OtroClienteDeServicio_RecibeClienteNoPermitido_SinEntrega(string cliente)
    {
        var entrega = Substitute.For<IEntregaConfiguracionIa>();

        await Falla(() => Servicio(entrega).ObtenerConfiguracionIa(Request(), new Contexto(Ct, cliente)), StatusCode.PermissionDenied, "CLIENTE_NO_PERMITIDO");

        await entrega.DidNotReceiveWithAnyArgs().EntregarAsync(default!, Ct);
    }

    [Theory]
    [InlineData("ocr")]
    [InlineData("dr_flit")]
    [InlineData("DIAGNOSTICO_DICTAMEN")]
    public async Task OtraHerramienta_RecibeHerramientaNoPermitida_SinSobre(string tool)
    {
        var entrega = Substitute.For<IEntregaConfiguracionIa>();

        await Falla(() => Servicio(entrega).ObtenerConfiguracionIa(Request(tool), new Contexto(Ct)), StatusCode.PermissionDenied, "HERRAMIENTA_NO_PERMITIDA");

        await entrega.DidNotReceiveWithAnyArgs().EntregarAsync(default!, Ct);
        _lector.Lecturas.Should().Be(0);
    }

    [Fact]
    public Task SinHerramienta_RecibeToolCodeRequerido() =>
        Falla(() => Servicio().ObtenerConfiguracionIa(Request(""), new Contexto(Ct)), StatusCode.InvalidArgument, "TOOL_CODE_REQUERIDO");

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public Task NonceQueNoEsDe16Bytes_RecibeNonceInvalido(int n) =>
        Falla(() => Servicio().ObtenerConfiguracionIa(Request(nonce: Nonce(n)), new Contexto(Ct)), StatusCode.InvalidArgument, "NONCE_INVALIDO");

    [Theory]
    [InlineData("")]
    [InlineData("Diag-DEV")]
    [InlineData("diag\ndev")]
    public Task KidMalFormado_RecibeKidInvalido(string kid) =>
        Falla(() => Servicio().ObtenerConfiguracionIa(Request(kid: kid), new Contexto(Ct)), StatusCode.InvalidArgument, "KID_INVALIDO");

    [Fact]
    public Task KidNoRegistrado_RecibeClaveDestinatarioDesconocida() =>
        Falla(() => Servicio().ObtenerConfiguracionIa(Request(kid: "diag-dev-209901"), new Contexto(Ct)), StatusCode.FailedPrecondition, "CLAVE_DESTINATARIO_DESCONOCIDA");

    [Fact]
    public async Task SinLlaveEnCoreApi_EsOkConDisponibleFalse_YSinProveedorConLlave()
    {
        _probe.HasApiKey(Arg.Any<string>()).Returns(false);

        var r = await Servicio().ObtenerConfiguracionIa(Request(), new Contexto(Ct));

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoNoDisponible.SinProveedorConLlave);
        r.Cola.Should().BeEmpty();
        r.TtlSegundos.Should().Be(60);
        r.Nonce.Should().Equal(Nonce());
        r.Kid.Should().Be(Kid);
    }

    [Fact]
    public async Task SinFila_EsOkConHerramientaSinConfigurar()
    {
        _repo.GetByToolCodeAsync(Tool, Arg.Any<CancellationToken>()).Returns((AiToolProviderSettings?)null);

        var r = await Servicio().ObtenerConfiguracionIa(Request(), new Contexto(Ct));

        r.Disponible.Should().BeFalse();
        r.Motivo.Should().Be(MotivoNoDisponible.HerramientaSinConfigurar);
    }

    [Fact]
    public async Task ExcepcionInterna_EsInternalGenerico_SinElTextoDeLaExcepcion()
    {
        var entrega = Substitute.For<IEntregaConfiguracionIa>();
        entrega.ConoceKid("diagnostico", Kid).Returns(true);
        entrega.EntregarAsync(default!, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("detalle-interno " + Centinela));

        var ex = (await FluentActions.Awaiting(() => Servicio(entrega).ObtenerConfiguracionIa(Request(), new Contexto(Ct)))
            .Should().ThrowAsync<RpcException>()).Which;

        ex.StatusCode.Should().Be(StatusCode.Internal);
        ex.Status.Detail.Should().NotContain("detalle-interno").And.NotContain("CENTINELA");
        _logger.Entradas.Should().NotContain(e => e.Texto.Contains("detalle-interno", StringComparison.Ordinal) || e.Texto.Contains("CENTINELA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CadaEntrega_SeAuditaConSubJtiToolKidRowVersionProveedoresEIp_SinLlaveNiSobre()
    {
        var r = await Servicio().ObtenerConfiguracionIa(Request(), new Contexto(Ct));

        var e = _logger.Entradas.Should().ContainSingle(x => x.Texto.StartsWith("ia.llave.entregada", StringComparison.Ordinal)).Which;
        e.Nivel.Should().Be(LogLevel.Information);
        e.Campos.Should().Contain(new KeyValuePair<string, string?>("Sub", "svc-diagnostico"))
            .And.Contain(new KeyValuePair<string, string?>("Jti", "jti-de-prueba"))
            .And.Contain(new KeyValuePair<string, string?>("Tool", Tool))
            .And.Contain(new KeyValuePair<string, string?>("Kid", Kid))
            .And.Contain(new KeyValuePair<string, string?>("RowVersion", "7"))
            .And.Contain(new KeyValuePair<string, string?>("Proveedores", "1"))
            .And.Contain(new KeyValuePair<string, string?>("Ip", "ipv4:10.0.0.9:51000"));

        var sobre = r.Cola[0].Llave;
        var todo = string.Join("\n", _logger.Entradas.Select(x => x.Texto + string.Join(",", x.Campos.Select(c => c.Value))));
        todo.Should().NotContain("CENTINELA").And.NotContain(sobre.TextoCifrado.ToBase64()).And.NotContain(sobre.CekCifrada.ToBase64());
    }

    [Fact]
    public async Task CadaRechazo_SeAuditaComoDenegadaConElMotivo()
    {
        await Falla(() => Servicio().ObtenerConfiguracionIa(Request("ocr"), new Contexto(Ct)), StatusCode.PermissionDenied, "HERRAMIENTA_NO_PERMITIDA");

        var e = _logger.Entradas.Should().ContainSingle(x => x.Texto.StartsWith("ia.llave.denegada", StringComparison.Ordinal)).Which;
        e.Nivel.Should().Be(LogLevel.Warning);
        e.Campos.Should().Contain(new KeyValuePair<string, string?>("Reason", "HERRAMIENTA_NO_PERMITIDA"))
            .And.Contain(new KeyValuePair<string, string?>("Sub", "svc-diagnostico"));
    }

    private sealed class Lector : ILectorLlaveProveedorIa
    {
        public string? Llave { get; set; }

        public int Lecturas { get; private set; }

        public string? LeerLlave(string providerCode)
        {
            Lecturas++;
            return providerCode == AiProviderCodes.Anthropic ? Llave : null;
        }
    }

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class LoggerCapturado : ILogger<ConfiguracionIaGrpcService>
    {
        public List<(LogLevel Nivel, string Texto, List<KeyValuePair<string, string?>> Campos)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var campos = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? kv.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value?.ToString())).ToList()
                : [];
            Entradas.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty), campos));
        }
    }

    private sealed class Contexto : ServerCallContext
    {
        private readonly CancellationToken _ct;

        public Contexto(CancellationToken ct, string cliente = "svc-diagnostico")
        {
            _ct = ct;
            UserState["flit.serviceCaller"] = new PlatformServiceCaller(cliente, Guid.Empty);
            UserState["__HttpContext"] = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", cliente), new Claim("jti", "jti-de-prueba")], "Bearer")),
            };
        }

        protected override string MethodCore => "/flit.platform.v1.ConfiguracionIaService/ObtenerConfiguracionIa";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:10.0.0.9:51000";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = [];
        protected override CancellationToken CancellationTokenCore => _ct;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, []);

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
