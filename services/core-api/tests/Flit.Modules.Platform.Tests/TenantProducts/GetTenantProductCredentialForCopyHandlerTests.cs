using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14694 (D-CRED-DIAG, DA-2=b, DA-3 residual; flit.identidad.v1.CredencialesProductoService.ObtenerCredencialImpuestoParaCopia)
/// — la llave <c>diagnostico/impuesto_api_key</c> se entrega para copiarla AUNQUE Diagnóstico esté apagado (no consulta el
/// acceso), y cada lectura queda auditada (con o sin fila) antes de descifrar. Sin efectos sobre la credencial.
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(tenantId, "svc-impuestos")</c> → disponible, valor y versión.</para>
/// </summary>
public sealed class GetTenantProductCredentialForCopyHandlerTests
{
    private const string Llave = "llave-impuesto-CENTINELA-copia";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITenantProductCredentialRepository _credentials = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();

    private GetTenantProductCredentialForCopyHandler Handler() => new(_credentials, _protector);

    private void ConFila() =>
        _credentials.GetAsync(TenantId, "diagnostico", "impuesto_api_key", Arg.Any<CancellationToken>())
            .Returns(new TenantProductCredential(TenantId, "diagnostico", "impuesto_api_key", "cifrado-opaco", RowVersion: 11, DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task ConFila_EntregaLaLlaveYLaVersion_YAuditaLaLectura()
    {
        ConFila();
        _protector.Unprotect(TenantId, "impuesto_api_key", "cifrado-opaco").Returns(Llave);

        var entrega = await Handler().HandleAsync(TenantId, "svc-impuestos", Ct);

        entrega.Disponible.Should().BeTrue();
        entrega.Value.Should().Be(Llave);
        entrega.Version.Should().Be(11);
        entrega.ToString().Should().NotContain(Llave);
        await _credentials.Received(1).RegistrarLecturaParaCopiaAsync(TenantId, "diagnostico", "impuesto_api_key", "svc-impuestos", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFila_NoDisponible_SinDescifrar_YTambienSeAudita()
    {
        var entrega = await Handler().HandleAsync(TenantId, "svc-impuestos", Ct);

        entrega.Disponible.Should().BeFalse();
        entrega.Motivo.Should().Be(MotivoCredencialImpuesto.SinConfigurar);
        entrega.Version.Should().Be(0);
        _protector.DidNotReceiveWithAnyArgs().Unprotect(default, default!, default!);
        await _credentials.Received(1).RegistrarLecturaParaCopiaAsync(TenantId, "diagnostico", "impuesto_api_key", "svc-impuestos", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoConsultaSiDiagnosticoEstaEncendido_NiEscribeLaCredencial()
    {
        ConFila();
        _protector.Unprotect(default, default!, default!).ReturnsForAnyArgs(Llave);

        await Handler().HandleAsync(TenantId, "svc-impuestos", Ct);

        // El handler no recibe IProductAccessStore: la copia no depende de que Diagnóstico esté encendido (DA-3 residual).
        typeof(GetTenantProductCredentialForCopyHandler).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).Should().NotContain(typeof(Flit.Modules.Platform.Domain.Access.IProductAccessStore));
        await _credentials.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default!, default!, default, Ct);
        await _credentials.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default!, default, Ct);
    }

    [Fact]
    public async Task SiLaAuditoriaFalla_NoEntregaLaLlave()
    {
        ConFila();
        _credentials.RegistrarLecturaParaCopiaAsync(default, default!, default!, default!, default, Ct)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("base caída")));

        var act = () => Handler().HandleAsync(TenantId, "svc-impuestos", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _protector.DidNotReceiveWithAnyArgs().Unprotect(default, default!, default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SinLector_SeRechaza(string lector)
    {
        var act = () => Handler().HandleAsync(TenantId, lector, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
        await _credentials.DidNotReceiveWithAnyArgs().GetAsync(default, default!, default!, Ct);
    }
}
