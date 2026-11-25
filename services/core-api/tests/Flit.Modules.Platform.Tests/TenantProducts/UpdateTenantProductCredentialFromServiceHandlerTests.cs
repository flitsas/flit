using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14694 (M04b, D-ALTA-IDEM, plan §5.8; <c>CredencialesProductoService.ActualizarCredencialImpuestoDiagnostico</c>) — cuando el
/// alta del proveedor rota la llave, core-impuestos propaga la nueva a Diagnóstico: el handler cifra y REEMPLAZA la fila
/// <c>diagnostico/impuesto_api_key</c> que ya existe (nunca la crea), audita con el cliente de servicio y no mira el acceso al
/// producto. Una llave con forma inválida se rechaza sin cifrar ni escribir.
/// <para>Uso de ejemplo: <c>await handler.HandleAsync(tenantId, nuevaLlave, "svc-impuestos")</c> → <c>true</c> si reemplazó.</para>
/// </summary>
public sealed class UpdateTenantProductCredentialFromServiceHandlerTests
{
    private const string Llave = "llave-nueva-CENTINELA-rotada-0123456789";
    private const string Cifrada = "cifrado-opaco-de-la-llave-nueva";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITenantProductCredentialRepository _credentials = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();

    public UpdateTenantProductCredentialFromServiceHandlerTests()
    {
        _protector.Protect(TenantId, "impuesto_api_key", Llave).Returns(Cifrada);
    }

    private UpdateTenantProductCredentialFromServiceHandler Handler() => new(_credentials, _protector);

    [Fact]
    public async Task ConFila_ReemplazaLaLlaveCifrada_ConElClienteEnLaAuditoria()
    {
        _credentials.ReplaceExistingFromServiceAsync(TenantId, "diagnostico", "impuesto_api_key", Cifrada, "svc-impuestos", Arg.Any<CancellationToken>())
            .Returns(true);

        var actualizada = await Handler().HandleAsync(TenantId, Llave, "svc-impuestos", Ct);

        actualizada.Should().BeTrue();
        await _credentials.Received(1).ReplaceExistingFromServiceAsync(
            TenantId, "diagnostico", "impuesto_api_key", Cifrada, "svc-impuestos", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinFila_NoLaCrea_YDevuelveFalse()
    {
        _credentials.ReplaceExistingFromServiceAsync(default, default!, default!, default!, default!, Ct).ReturnsForAnyArgs(false);

        var actualizada = await Handler().HandleAsync(TenantId, Llave, "svc-impuestos", Ct);

        actualizada.Should().BeFalse();
        await _credentials.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public async Task NoDependeDelAccesoAlProducto_NiLeeNiBorra()
    {
        _credentials.ReplaceExistingFromServiceAsync(default, default!, default!, default!, default!, Ct).ReturnsForAnyArgs(true);

        await Handler().HandleAsync(TenantId, Llave, "svc-impuestos", Ct);

        typeof(UpdateTenantProductCredentialFromServiceHandler).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).Should().NotContain(typeof(Flit.Modules.Platform.Domain.Access.IProductAccessStore));
        _protector.DidNotReceiveWithAnyArgs().Unprotect(default, default!, default!);
        await _credentials.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default!, default, Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("llave con espacios 0123456789")]
    [InlineData("llave-con-salto-0123456789\n")]
    public async Task LlaveConFormaInvalida_CredentialInvalid_SinCifrarNiEscribir(string llave)
    {
        var act = () => Handler().HandleAsync(TenantId, llave, "svc-impuestos", Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.CredentialInvalid);
        _protector.DidNotReceiveWithAnyArgs().Protect(default, default!, default!);
        await _credentials.DidNotReceiveWithAnyArgs().ReplaceExistingFromServiceAsync(default, default!, default!, default!, default!, Ct);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SinServicio_SeRechaza(string servicio)
    {
        var act = () => Handler().HandleAsync(TenantId, Llave, servicio, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
        await _credentials.DidNotReceiveWithAnyArgs().ReplaceExistingFromServiceAsync(default, default!, default!, default!, default!, Ct);
    }

    [Fact]
    public async Task ElErrorNoMuestraLaLlave()
    {
        var act = () => Handler().HandleAsync(TenantId, Llave + " con espacio", "svc-impuestos", Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Message.Should().NotContain("CENTINELA");
    }
}
