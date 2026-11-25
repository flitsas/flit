using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #12958 (B-03) — reglas de negocio al encender o apagar un producto. La escritura y la
/// auditoría las prueba <c>PlatformProductsTests</c> contra Postgres real.
/// </summary>
public sealed class SetTenantProductEnabledHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private readonly IProductCatalog _catalog = Substitute.For<IProductCatalog>();
    private readonly ITenantProductRepository _repository = Substitute.For<ITenantProductRepository>();
    private readonly IProductAccessStore _access = Substitute.For<IProductAccessStore>();
    private readonly ITenantProductChangeJobs _jobs = Substitute.For<ITenantProductChangeJobs>();
    private readonly IPrecondicionActivacion _precondicion = Substitute.For<IPrecondicionActivacion>();

    public SetTenantProductEnabledHandlerTests()
    {
        _access.GetTenantChainAsync(TenantId, Arg.Any<CancellationToken>()).Returns([TenantId]);
        _catalog.FindAsync("comparendos", Arg.Any<CancellationToken>())
            .Returns(new Product("comparendos", "Comparendos", "ticket", ProductStatuses.Active));
        _catalog.FindAsync("viejo", Arg.Any<CancellationToken>())
            .Returns(new Product("viejo", "Viejo", "archive", ProductStatuses.Inactive));
        _catalog.FindAsync("impuestos", Arg.Any<CancellationToken>())
            .Returns(new Product("impuestos", "Impuestos", "receipt", ProductStatuses.Active));
        _precondicion.CumpleAsync(TenantId, "impuestos", Arg.Any<CancellationToken>()).Returns(true);
        // El repositorio de verdad invoca antesDeGuardar solo si hubo cambio (TenantProductChangeOutboxTests); aquí, siempre.
        _repository.SetAsync(default, default!, default, default, default, default, Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(ci =>
            {
                var change = new TenantProductChange(
                    new TenantProduct(ci.ArgAt<Guid>(0), ci.ArgAt<string>(1), ci.ArgAt<bool>(2), ci.ArgAt<string?>(3), DateTimeOffset.UtcNow, ci.ArgAt<Guid?>(4)),
                    Changed: true);
                ci.ArgAt<Action<TenantProductChange>?>(5)?.Invoke(change);
                return change;
            });
    }

    private SetTenantProductEnabledHandler Handler() => new(_catalog, _repository, _access, _jobs, _precondicion);

    [Fact]
    public async Task Encender_NormalizaElCodigoYLasNotas_YDelegaEnElRepositorio()
    {
        var result = await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "  Comparendos ", true, "  Piloto  "), AdminId, TestContext.Current.CancellationToken);

        result.Changed.Should().BeTrue();
        await _repository.Received(1).SetAsync(TenantId, "comparendos", true, "Piloto", AdminId, Arg.Any<Action<TenantProductChange>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotasEnBlanco_SeGuardanComoNulas()
    {
        await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "comparendos", false, "   "), AdminId, TestContext.Current.CancellationToken);

        await _repository.Received(1).SetAsync(TenantId, "comparendos", false, null, AdminId, Arg.Any<Action<TenantProductChange>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Plataforma_NoSeHabilitaPorEmpresa()
    {
        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "plataforma", false, null), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductAlwaysOn);
        await _repository.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProductoInexistente_SeRechaza()
    {
        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "flotas", true, null), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductNotFound);
    }

    [Fact]
    public async Task ProductoInactivo_NoSeEnciende()
    {
        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "viejo", true, null), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductInactive);
    }

    [Fact]
    public async Task ProductoInactivo_SiSePuedeApagar()
    {
        await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "viejo", false, null), AdminId, TestContext.Current.CancellationToken);

        await _repository.Received(1).SetAsync(TenantId, "viejo", false, null, AdminId, Arg.Any<Action<TenantProductChange>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotasDemasiadoLargas_SeRechazan()
    {
        var notes = new string('x', SetTenantProductEnabledHandler.NotesMaxLength + 1);
        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "comparendos", true, notes), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.NotesTooLong);
    }

    // HU #12966 — una hija no enciende un producto que su cabeza tiene apagado (ADR-0057)
    [Fact]
    public async Task EncenderEnHijaConCabezaApagada_SeRechaza()
    {
        var head = Guid.NewGuid();
        _access.GetTenantChainAsync(TenantId, Arg.Any<CancellationToken>()).Returns([TenantId, head]);
        _access.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), "comparendos", Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());

        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "comparendos", true, null), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductNotEnabledForHead);
    }

    [Fact]
    public async Task ApagarEnHija_NoConsultaLaCabeza()
    {
        await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "comparendos", false, null), AdminId, TestContext.Current.CancellationToken);

        await _access.DidNotReceiveWithAnyArgs().GetTenantChainAsync(default, TestContext.Current.CancellationToken);
    }

    // ── HU #14694 — producto con aprovisionamiento (plan §1.1 decisión E2, D-ACT, D-EMAIL) ───────────────────

    [Fact]
    public async Task EncenderImpuestos_SinAdminCompanyActivo_SeRechaza_YNoGuardaNiEncola()
    {
        _precondicion.CumpleAsync(TenantId, "impuestos", Arg.Any<CancellationToken>()).Returns(false);

        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "impuestos", true, null), AdminId, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<TenantProductException>()).Which;
        ex.Code.Should().Be(TenantProductException.ProvisioningPrerequisiteMissing);
        ex.Message.Should().Be("La compañía no tiene un Administrador de Compañía activo");
        await _repository.DidNotReceiveWithAnyArgs().SetAsync(default, default!, default, default, default, default, TestContext.Current.CancellationToken);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCambio(default!);
    }

    [Fact]
    public async Task EncenderImpuestos_ConAdminCompany_EncolaElTrabajoEnElGuardadoDelCambio()
    {
        var result = await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "impuestos", true, null), AdminId, TestContext.Current.CancellationToken);

        result.Changed.Should().BeTrue();
        _jobs.Received(1).EncolarCambio(Arg.Is<TenantProduct>(p => p.TenantId == TenantId && p.ProductCode == "impuestos" && p.Enabled));
        await _repository.Received(1).SetAsync(TenantId, "impuestos", true, null, AdminId, Arg.Is<Action<TenantProductChange>?>(a => a != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApagarImpuestos_NoExigeLaPrecondicion_YEncolaLaSuspension()
    {
        _precondicion.CumpleAsync(TenantId, "impuestos", Arg.Any<CancellationToken>()).Returns(false);

        await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "impuestos", false, null), AdminId, TestContext.Current.CancellationToken);

        await _precondicion.DidNotReceiveWithAnyArgs().CumpleAsync(default, default!, TestContext.Current.CancellationToken);
        _jobs.Received(1).EncolarCambio(Arg.Is<TenantProduct>(p => p.ProductCode == "impuestos" && !p.Enabled));
    }

    [Fact]
    public async Task ProductoSinAprovisionamiento_NiConsultaLaPrecondicion_NiEncola()
    {
        await Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "comparendos", true, null), AdminId, TestContext.Current.CancellationToken);

        await _precondicion.DidNotReceiveWithAnyArgs().CumpleAsync(default, default!, TestContext.Current.CancellationToken);
        _jobs.DidNotReceiveWithAnyArgs().EncolarCambio(default!);
        await _repository.Received(1).SetAsync(TenantId, "comparendos", true, null, AdminId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncenderImpuestosEnHijaConCabezaApagada_RechazaPorLaCabezaAntesQuePorLaPrecondicion()
    {
        var head = Guid.NewGuid();
        _access.GetTenantChainAsync(TenantId, Arg.Any<CancellationToken>()).Returns([TenantId, head]);
        _access.GetTenantsWithProductEnabledAsync(Arg.Any<IReadOnlyList<Guid>>(), "impuestos", Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid>());
        _precondicion.CumpleAsync(TenantId, "impuestos", Arg.Any<CancellationToken>()).Returns(false);

        var act = () => Handler().HandleAsync(new SetTenantProductEnabledCommand(TenantId, "impuestos", true, null), AdminId, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductNotEnabledForHead);
    }

    [Theory]
    [InlineData("impuestos", true)]
    [InlineData(" IMPUESTOS ", true)]
    [InlineData("diagnostico", false)]
    [InlineData("tramites", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ProductosConAprovisionamiento_HoySoloImpuestos(string? code, bool esperado) =>
        ProductosConAprovisionamiento.Tiene(code).Should().Be(esperado);
}
