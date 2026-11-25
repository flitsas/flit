using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// ADR-0092 (diseño impuesto-real §1.4) — guardar la API key del impuesto de una empresa: lista blanca
/// <c>(diagnostico, impuesto_api_key)</c>, valor <c>^[\x21-\x7E]{16,256}$</c>, cifrado con la empresa y el código antes del
/// repositorio y sin eco del valor en errores ni en <c>ToString()</c>.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <c>await new SetTenantProductCredentialHandler(repo, protector).HandleAsync(new SetTenantProductCredentialCommand(tenant, "diagnostico", "impuesto_api_key", llave), adminId, ct)</c>.
/// </remarks>
public sealed class SetTenantProductCredentialHandlerTests
{
    private const string Llave = "llave-de-prueba-CENTINELA-0123456789";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly ITenantProductCredentialRepository _repository = Substitute.For<ITenantProductCredentialRepository>();
    private readonly ITenantProductSecretProtector _protector = Substitute.For<ITenantProductSecretProtector>();

    public SetTenantProductCredentialHandlerTests()
    {
        _protector.Protect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => $"cifrado:{ci.ArgAt<Guid>(0):N}:{ci.ArgAt<string>(1)}:{ci.ArgAt<string>(2).Length}");
        _repository.SetAsync(default, default!, default!, default!, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(TenantProductCredentialWrite.Created);
    }

    private SetTenantProductCredentialHandler Handler() => new(_repository, _protector);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ValorValido_SeCifraConEmpresaYCodigo_YAlRepositorioSoloLlegaElCifrado()
    {
        var result = await Handler().HandleAsync(
            new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", Llave), AdminId, Ct);

        result.Should().Be(TenantProductCredentialWrite.Created);
        _protector.Received(1).Protect(TenantId, "impuesto_api_key", Llave);
        await _repository.Received(1).SetAsync(
            TenantId, "diagnostico", "impuesto_api_key", $"cifrado:{TenantId:N}:impuesto_api_key:{Llave.Length}", AdminId, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(s => s.Contains(Llave, StringComparison.Ordinal)), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reemplazar_DevuelveLoQueInformaElRepositorio()
    {
        _repository.SetAsync(default, default!, default!, default!, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(TenantProductCredentialWrite.Replaced);

        var result = await Handler().HandleAsync(
            new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", Llave), AdminId, Ct);

        result.Should().Be(TenantProductCredentialWrite.Replaced);
    }

    [Fact]
    public async Task CodigosConMayusculasYEspacios_SeNormalizan()
    {
        await Handler().HandleAsync(
            new SetTenantProductCredentialCommand(TenantId, " Diagnostico ", "IMPUESTO_API_KEY", Llave), AdminId, Ct);

        await _repository.Received(1).SetAsync(
            TenantId, "diagnostico", "impuesto_api_key", Arg.Any<string>(), AdminId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(256)]
    public async Task LimitesDeLongitud_SeAceptan(int largo)
    {
        var valor = new string('k', largo);

        await Handler().HandleAsync(new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", valor), AdminId, Ct);

        _protector.Received(1).Protect(TenantId, "impuesto_api_key", valor);
    }

    public static TheoryData<string?> ValoresInvalidos() =>
    [
        (string?)null,
        "",
        "corta-15-chars!",
        new string('x', 257),
        "llave con espacio-0123456789",
        "llave-con-tab\t-0123456789",
        "llave-con-eñe-0123456789",
        "llave-con-salto-0123456789\n",
    ];

    [Theory]
    [MemberData(nameof(ValoresInvalidos))]
    public async Task ValorInvalido_400SinEco_YNoSeCifraNiGuarda(string? valor)
    {
        var act = () => Handler().HandleAsync(new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", valor), AdminId, Ct);

        var ex = (await act.Should().ThrowAsync<TenantProductException>()).Which;
        ex.Code.Should().Be(TenantProductException.CredentialInvalid);
        ex.Message.Should().Be(SetTenantProductCredentialHandler.InvalidValueMessage);
        if (!string.IsNullOrEmpty(valor))
            ex.ToString().Should().NotContain(valor);
        _protector.DidNotReceiveWithAnyArgs().Protect(default, default!, default!);
        await _repository.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default!, default!, default, Ct);
    }

    [Theory]
    [InlineData("tramites", "impuesto_api_key")]
    [InlineData("diagnostico", "otra_llave")]
    [InlineData("plataforma", "impuesto_api_key")]
    [InlineData("", "")]
    // HU #14711: los secretos del webhook solo los escribe el alta del proveedor; el PUT/DELETE manual no los acepta.
    [InlineData("diagnostico", "impuesto_webhook_secret")]
    [InlineData("diagnostico", "impuesto_webhook_secret_anterior")]
    public async Task CombinacionFueraDeLaListaBlanca_NoSoportada(string producto, string credencial)
    {
        var act = () => Handler().HandleAsync(new SetTenantProductCredentialCommand(TenantId, producto, credencial, Llave), AdminId, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.CredentialNotSupported);
        _protector.DidNotReceiveWithAnyArgs().Protect(default, default!, default!);
    }

    [Fact]
    public async Task CifradoFueraDeRango_NoSeGuarda()
    {
        _protector.Protect(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>()).Returns("corto");

        var act = () => Handler().HandleAsync(new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", Llave), AdminId, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().NotContain(Llave);
        await _repository.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default!, default!, default, Ct);
    }

    [Fact]
    public void ElComandoNoMuestraLaLlaveEnToString()
    {
        var command = new SetTenantProductCredentialCommand(TenantId, "diagnostico", "impuesto_api_key", Llave);

        command.ToString().Should().NotContain(Llave).And.Contain("diagnostico").And.Contain("impuesto_api_key");
    }
}
