using Flit.Admin.Application.Companies.TransitOffices.SetOtBlockingPolicy;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtBlockingPolicies;

/// <summary>
/// Tests del caso de uso de fijar políticas de bloqueo por OT (FEATURE 05). Ejercita el handler con
/// dependencias mockeadas (NSubstitute) para aislar la validación del orden AC3 (existe en catálogo →
/// habilitado para la compañía) y AC4 (criterio configurable). La persistencia real (upsert +
/// auditoría) se cubre en <c>OtBlockingPolicyRepositoryTests</c>.
/// </summary>
public sealed class SetOtBlockingPolicyHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ChangedBy = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task AC3_TransitOfficeIdVacio_RetornaInvalid_SinTocarRepo()
    {
        var (catalog, grants, repository) = Mocks();
        var handler = new SetOtBlockingPolicyHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(Command(officeId: Guid.Empty), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtBlockingPolicyHandler.TransitOfficeNotFoundMessage);
        catalog.DidNotReceive().Exists(Arg.Any<Guid>());
        await AssertNoSet(repository);
    }

    [Fact]
    public async Task AC3_TransitOfficeIdNoExisteEnCatalogo_RetornaInvalid_SinTocarRepo()
    {
        var (catalog, grants, repository) = Mocks();
        catalog.Exists(TransitOfficeId).Returns(false);
        var handler = new SetOtBlockingPolicyHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtBlockingPolicyHandler.TransitOfficeNotFoundMessage);
        await grants.DidNotReceive().ListEnabledOfficeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await AssertNoSet(repository);
    }

    [Fact]
    public async Task AC3_TransitOfficeNoHabilitadoParaLaCompania_RetornaInvalid_ConMensaje()
    {
        var (catalog, grants, repository) = Mocks();
        catalog.Exists(TransitOfficeId).Returns(true);
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([]));
        var handler = new SetOtBlockingPolicyHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(Command(), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtBlockingPolicyHandler.TransitOfficeNotGrantedMessage);
        await AssertNoSet(repository);
    }

    [Theory]
    [InlineData("vehicle")] // no configurable (rompería field_values/FUR).
    [InlineData("gravamenes")] // fuera del vocabulario.
    [InlineData("")]
    [InlineData(null)]
    public async Task AC4_CriterioInvalido_RetornaInvalid(string? criterion)
    {
        var (catalog, grants, repository) = Mocks();
        catalog.Exists(TransitOfficeId).Returns(true);
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([TransitOfficeId]));
        var handler = new SetOtBlockingPolicyHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(Command(criterion: criterion), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "criterion" && e.Message == SetOtBlockingPolicyHandler.InvalidCriterionMessage);
        await AssertNoSet(repository);
    }

    [Theory]
    [InlineData(BlockingCriteria.Soat)]
    [InlineData(BlockingCriteria.Rtm)]
    [InlineData(BlockingCriteria.EstadoVehiculo)]
    [InlineData(BlockingCriteria.Fines)]
    [InlineData(BlockingCriteria.Rnmc)]
    public async Task Happy_TodoValido_LlamaSetAsync_ConLosArgsExactos(string criterion)
    {
        var correlationId = Guid.NewGuid();
        var (catalog, grants, repository) = Mocks();
        catalog.Exists(TransitOfficeId).Returns(true);
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([TransitOfficeId]));
        var handler = new SetOtBlockingPolicyHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtBlockingPolicyCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            Criterion = criterion,
            Blocks = false,
            ChangedBy = ChangedBy,
            CorrelationId = correlationId,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        await repository.Received(1).SetAsync(
            TenantId, TransitOfficeId, criterion, false, ChangedBy, correlationId,
            Arg.Any<CancellationToken>());
    }

    private static (ITransitOfficeCatalog, ITransitGrantRepository, IOtBlockingPolicyRepository) Mocks() =>
        (Substitute.For<ITransitOfficeCatalog>(),
         Substitute.For<ITransitGrantRepository>(),
         Substitute.For<IOtBlockingPolicyRepository>());

    private static SetOtBlockingPolicyCommand Command(
        Guid? officeId = null, string? criterion = BlockingCriteria.Soat) =>
        new()
        {
            TenantId = TenantId,
            TransitOfficeId = officeId ?? TransitOfficeId,
            Criterion = criterion,
            Blocks = false,
            ChangedBy = ChangedBy,
        };

    private static Task AssertNoSet(IOtBlockingPolicyRepository repository) =>
        repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
}
