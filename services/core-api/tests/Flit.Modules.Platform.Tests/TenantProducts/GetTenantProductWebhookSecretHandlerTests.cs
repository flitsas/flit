using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.TenantProducts;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14711 (plan §3.2) — secreto del webhook del impuesto para core-consultas: el actual
/// (<c>diagnostico/impuesto_webhook_secret</c>) con su <c>row_version</c> como versión y el anterior
/// (<c>impuesto_webhook_secret_anterior</c>) solo mientras <c>updated_at + 24 h</c> siga en el futuro. Fail-closed con
/// Diagnóstico apagado (no descifra) y sin fila del actual. Cada secreto se descifra con su propio código.
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(tenantId, ct)</c> → configurado, actual, anterior vigente y versión.</para>
/// </summary>
public sealed class GetTenantProductWebhookSecretHandlerTests
{
    private const string Actual = "whsec_CENTINELA_actual_0123456789";
    private const string Anterior = "whsec_CENTINELA_anterior_0123456789";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Cabeza = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProductAccessStore _access = Substitute.For<IProductAccessStore>();
    private readonly ITenantProductCredentialRepository _credentials = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();

    public GetTenantProductWebhookSecretHandlerTests()
    {
        _access.GetTenantChainAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new[] { TenantId, Cabeza });
        Encendidas(TenantId, Cabeza);
        _protector.Unprotect(TenantId, "impuesto_webhook_secret", "cifrado-actual").Returns(Actual);
        _protector.Unprotect(TenantId, "impuesto_webhook_secret_anterior", "cifrado-anterior").Returns(Anterior);
    }

    private readonly Capturador _log = new();

    private GetTenantProductWebhookSecretHandler Handler() => new(_access, _credentials, _protector, new Reloj(Ahora), _log);

    private void Encendidas(params Guid[] empresas) =>
        _access.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), "diagnostico", Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>(empresas));

    private void ConActual(long version = 9) =>
        _credentials.GetAsync(TenantId, "diagnostico", "impuesto_webhook_secret", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_webhook_secret", "cifrado-actual", version, Ahora.AddDays(-1)));

    private void ConAnterior(DateTimeOffset actualizadoEn) =>
        _credentials.GetAsync(TenantId, "diagnostico", "impuesto_webhook_secret_anterior", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_webhook_secret_anterior", "cifrado-anterior", 3, actualizadoEn));

    [Fact]
    public async Task SoloActual_EntregaElActualConSuRowVersion_SinAnterior()
    {
        ConActual(version: 9);

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeTrue();
        entrega.Actual.Should().Be(Actual);
        entrega.Anterior.Should().BeNull();
        entrega.AnteriorVenceEn.Should().BeNull();
        entrega.Version.Should().Be(9);
    }

    [Fact]
    public async Task AnteriorVigente_SeEntregaConSuVencimientoA24HorasDeSuUpdatedAt()
    {
        ConActual();
        ConAnterior(Ahora.AddHours(-23));

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Anterior.Should().Be(Anterior);
        entrega.AnteriorVenceEn.Should().Be(Ahora.AddHours(1));
        _protector.Received(1).Unprotect(TenantId, "impuesto_webhook_secret_anterior", "cifrado-anterior");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(48)]
    public async Task AnteriorVencido_NoSeEntregaNiSeDescifra(int horas)
    {
        ConActual();
        ConAnterior(Ahora.AddHours(-horas));

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeTrue();
        entrega.Anterior.Should().BeNull();
        entrega.AnteriorVenceEn.Should().BeNull();
        _protector.DidNotReceive().Unprotect(TenantId, "impuesto_webhook_secret_anterior", Arg.Any<string>());
    }

    [Fact]
    public async Task SinActual_NoConfigurado_SinConfigurar_Version0_AunqueHayaAnterior()
    {
        ConAnterior(Ahora.AddHours(-1));

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeFalse();
        entrega.Motivo.Should().Be(MotivoCredencialImpuesto.SinConfigurar);
        entrega.Actual.Should().BeNull();
        entrega.Anterior.Should().BeNull();
        entrega.Version.Should().Be(0);
        _protector.DidNotReceiveWithAnyArgs().Unprotect(default, default!, default!);
    }

    [Fact]
    public async Task DiagnosticoApagadoEnUnAncestro_ProductoApagado_SinLeerNiDescifrar()
    {
        ConActual();
        Encendidas(TenantId);

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeFalse();
        entrega.Motivo.Should().Be(MotivoCredencialImpuesto.ProductoApagado);
        _protector.DidNotReceiveWithAnyArgs().Unprotect(default, default!, default!);
        await _credentials.DidNotReceiveWithAnyArgs().GetAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task EmpresaInexistente_ProductoApagado()
    {
        _access.GetTenantChainAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeFalse();
        entrega.Motivo.Should().Be(MotivoCredencialImpuesto.ProductoApagado);
    }

    [Fact]
    public async Task ToString_NoMuestraNingunSecreto()
    {
        ConActual();
        ConAnterior(Ahora.AddHours(-1));

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.ToString().Should().NotContain(Actual).And.NotContain(Anterior).And.Contain("Version = 9");
    }

    [Fact]
    public async Task NoEscribeNiAuditaCredenciales()
    {
        ConActual();

        await Handler().HandleAsync(TenantId, Ct);

        await _credentials.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default!, default!, default, Ct);
        await _credentials.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task L3_AnteriorQueNoSeDescifra_SeIgnora_SeEntregaElActual_YWarningSinValores()
    {
        ConActual(version: 9);
        ConAnterior(Ahora.AddHours(-1));
        _protector.Unprotect(TenantId, "impuesto_webhook_secret_anterior", "cifrado-anterior")
            .Throws(new CryptographicException("detalle-de-la-llave-que-no-va-al-log"));

        var entrega = await Handler().HandleAsync(TenantId, Ct);

        entrega.Configurado.Should().BeTrue();
        entrega.Actual.Should().Be(Actual);
        entrega.Anterior.Should().BeNull();
        entrega.AnteriorVenceEn.Should().BeNull();
        entrega.Version.Should().Be(9);
        _log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning && e.Id == 7424);
        var todo = string.Join("|", _log.Entradas.Select(e => e.Texto));
        todo.Should().NotContain(Actual).And.NotContain(Anterior).And.NotContain("cifrado-anterior")
            .And.NotContain("detalle-de-la-llave-que-no-va-al-log");
    }

    [Fact]
    public async Task L3_ActualQueNoSeDescifra_SiguePropagando()
    {
        ConActual();
        _protector.Unprotect(TenantId, "impuesto_webhook_secret", "cifrado-actual").Throws(new CryptographicException("x"));

        var act = () => Handler().HandleAsync(TenantId, Ct);

        await act.Should().ThrowAsync<CryptographicException>("sin el actual no hay con qué verificar; lo traduce el rpc");
    }

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class Capturador : ILogger<GetTenantProductWebhookSecretHandler>
    {
        public List<(LogLevel Nivel, int Id, string Texto)> Entradas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entradas.Add((logLevel, eventId.Id, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
    }
}
