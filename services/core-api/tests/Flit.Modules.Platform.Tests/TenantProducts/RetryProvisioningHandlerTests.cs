using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Platform.Domain.TenantProducts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Platform.Tests.TenantProducts;

/// <summary>
/// HU #14694 (plan §1.2 decisión R1, platform.v1 1.1.0) — «Reintentar» encola el trabajo
/// <c>impuestos.aprovisionamiento.reintentar</c> con el id del SuperAdmin; la plataforma no mira el estado.
/// <para>Uso de ejemplo: <c>handler.HandleAsync(new RetryProvisioningCommand(tenantId, "impuestos"), superAdminId)</c>.</para>
/// </summary>
public sealed class RetryProvisioningHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SuperAdminId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ITenantProductRepository _repository = Substitute.For<ITenantProductRepository>();
    private readonly ITenantProductChangeJobs _jobs = Substitute.For<ITenantProductChangeJobs>();

    private RetryProvisioningHandler Handler() => new(_repository, _jobs);

    private void Estado(bool? enabled) =>
        _repository.GetAsync(TenantId, "impuestos", Arg.Any<CancellationToken>())
            .Returns(enabled is null ? null : new TenantProduct(TenantId, "impuestos", enabled.Value, null, DateTimeOffset.UtcNow, null));

    [Fact]
    public async Task ProductoEncendido_EncolaElReintentoConQuienLoPidio()
    {
        Estado(true);

        await Handler().HandleAsync(new RetryProvisioningCommand(TenantId, " Impuestos "), SuperAdminId, Ct);

        await _jobs.Received(1).EncolarReintentoAsync(TenantId, "impuestos", SuperAdminId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ProductoApagadoOSinFila_SeRechazaConProductNotEnabled(bool? enabled)
    {
        Estado(enabled);

        var act = () => Handler().HandleAsync(new RetryProvisioningCommand(TenantId, "impuestos"), SuperAdminId, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductNotEnabled);
        await _jobs.DidNotReceiveWithAnyArgs().EncolarReintentoAsync(default, default!, default, Ct);
    }

    [Theory]
    [InlineData("diagnostico")]
    [InlineData("tramites")]
    [InlineData("flotas")]
    public async Task ProductoSinAprovisionamiento_SeRechazaConProductWithoutProvisioning(string code)
    {
        var act = () => Handler().HandleAsync(new RetryProvisioningCommand(TenantId, code), SuperAdminId, Ct);

        (await act.Should().ThrowAsync<TenantProductException>()).Which.Code.Should().Be(TenantProductException.ProductWithoutProvisioning);
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default!, Ct);
        await _jobs.DidNotReceiveWithAnyArgs().EncolarReintentoAsync(default, default!, default, Ct);
    }
}
