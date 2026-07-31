using Flit.Admin.Application.Companies.TransitOffices.SetOtConsultationRestriction;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtConsultationRestrictions;

/// <summary>
/// Tests del caso de uso de fijar restricciones de consulta por OT (HU #10759). Ejercita el
/// handler con dependencias mockeadas (NSubstitute) para aislar la lógica de validación del
/// orden AC3 (existe en catálogo → habilitado para la compañía) y AC4 (kind restringible).
/// La persistencia real (upsert + auditoría) se cubre en <c>OtConsultationRestrictionRepositoryTests</c>.
/// </summary>
public sealed class SetOtConsultationRestrictionHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    private static readonly Guid ChangedBy = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---------- AC3: OT inexistente / Guid.Empty ----------

    [Fact]
    public async Task AC3_TransitOfficeIdVacio_RetornaInvalid_SinTocarRepo()
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        var grants = Substitute.For<ITransitGrantRepository>();
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = Guid.Empty,
            ConsultationKind = RestrictedConsultationKinds.Rnmc,
            Enabled = false,
            ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtConsultationRestrictionHandler.TransitOfficeNotFoundMessage);
        catalog.DidNotReceive().Exists(Arg.Any<Guid>());
        await grants.DidNotReceive().ListEnabledOfficeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_TransitOfficeIdNoExisteEnCatalogo_RetornaInvalid_SinTocarRepo()
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(TransitOfficeId).Returns(false);
        var grants = Substitute.For<ITransitGrantRepository>();
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            ConsultationKind = RestrictedConsultationKinds.Rnmc,
            Enabled = false,
            ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtConsultationRestrictionHandler.TransitOfficeNotFoundMessage);
        await grants.DidNotReceive().ListEnabledOfficeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ---------- AC3: OT no habilitado (sin grant) para la compañía ----------

    [Fact]
    public async Task AC3_TransitOfficeNoHabilitadoParaLaCompania_RetornaInvalid_ConMensaje()
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(TransitOfficeId).Returns(true);
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([]));
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            ConsultationKind = RestrictedConsultationKinds.Rnmc,
            Enabled = false,
            ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "transitOfficeId" && e.Message == SetOtConsultationRestrictionHandler.TransitOfficeNotGrantedMessage);
        await repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ---------- AC4: kind no configurable ----------

    [Theory]
    [InlineData("vehicle")] // excluido a propósito: rompería field_values/FUR (ver DDL).
    [InlineData("")]
    [InlineData(null)]
    public async Task AC4_ConsultationKindInvalido_RetornaInvalid(string? kind)
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(TransitOfficeId).Returns(true);
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([TransitOfficeId]));
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            ConsultationKind = kind,
            Enabled = false,
            ChangedBy = ChangedBy,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.Field == "consultationKind" && e.Message == SetOtConsultationRestrictionHandler.InvalidConsultationKindMessage);
        await repository.DidNotReceive().SetAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(),
            Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // ---------- Happy path ----------

    [Fact]
    public async Task Happy_TodoValido_LlamaSetAsync_ConLosArgsExactos()
    {
        var correlationId = Guid.NewGuid();
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(TransitOfficeId).Returns(true);
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([TransitOfficeId]));
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var result = await handler.HandleAsync(new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            ConsultationKind = RestrictedConsultationKinds.Fines,
            Enabled = false,
            ChangedBy = ChangedBy,
            CorrelationId = correlationId,
        }, TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        await repository.Received(1).SetAsync(
            TenantId, TransitOfficeId, RestrictedConsultationKinds.Fines, false, ChangedBy, correlationId,
            Arg.Any<CancellationToken>());
    }

    // ---------- Idempotencia: reenviar el mismo estado en ambos sentidos sigue siendo válido ----------

    [Fact]
    public async Task Idempotencia_ReenviarElMismoEstadoDeseado_SigueSiendoValido_YDelegaAlRepoAmbasVeces()
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.Exists(TransitOfficeId).Returns(true);
        var grants = Substitute.For<ITransitGrantRepository>();
        grants.ListEnabledOfficeIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([TransitOfficeId]));
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        var handler = new SetOtConsultationRestrictionHandler(catalog, grants, repository);

        var command = new SetOtConsultationRestrictionCommand
        {
            TenantId = TenantId,
            TransitOfficeId = TransitOfficeId,
            ConsultationKind = RestrictedConsultationKinds.Rnmc,
            Enabled = false,
            ChangedBy = ChangedBy,
        };

        var first = await handler.HandleAsync(command, TestContext.Current.CancellationToken);
        var second = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        first.IsValid.Should().BeTrue();
        second.IsValid.Should().BeTrue();
        // La idempotencia (no-op sin duplicar auditoría) la decide el repositorio; el handler
        // delega ambas veces con los mismos argumentos (ver OtConsultationRestrictionRepositoryTests).
        await repository.Received(2).SetAsync(
            TenantId, TransitOfficeId, RestrictedConsultationKinds.Rnmc, false, ChangedBy, null,
            Arg.Any<CancellationToken>());
    }
}
