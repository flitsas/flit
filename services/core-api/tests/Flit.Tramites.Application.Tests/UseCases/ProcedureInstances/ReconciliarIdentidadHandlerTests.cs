using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class ReconciliarIdentidadHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();
    private readonly ReconciliarIdentidadHandler _handler;

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _validation = Guid.NewGuid();

    public ReconciliarIdentidadHandlerTests() =>
        _handler = new ReconciliarIdentidadHandler(
            _repo, _kyverum, new IdentityValidationResultApplier(_events), Substitute.For<IIdentityValidationAuditLog>());

    private ProcedureInstanceBiometricValidation Seed(
        string status = BiometricEstados.EnProceso, string provider = "kyverum", string? verificationId = "kyv-1")
    {
        var v = new ProcedureInstanceBiometricValidation
        {
            Id = _validation,
            TenantId = _tenant,
            ProcedureInstanceId = _instance,
            PartyRole = "comprador",
            Status = status,
            Provider = provider,
            KyverumVerificationId = verificationId,
            Name = "X",
            DocumentType = "CC",
            DocumentNumber = "1",
            Email = "x@y.com",
            TokenHash = "h",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>()).Returns(v);
        return v;
    }

    [Fact]
    public async Task Reconcile_KyverumApproved_AppliesAndEmits()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        _kyverum.GetStatusAsync("kyv-1", "comprador", Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("aprobado", 88, "{}"));

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Updated.Should().BeTrue();
        result.Status.Should().Be(BiometricEstados.Aprobado);
        v.Status.Should().Be(BiometricEstados.Aprobado);
        v.ValidatedAt.Should().NotBeNull();
        await _events.Received(1).PublishAsync(Arg.Is<IdentityValidationCompleted>(e =>
            e.ValidationId == v.Id && e.Estado == BiometricEstados.Aprobado), ct);
        await _repo.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Reconcile_StillPending_NoChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var v = Seed();
        _kyverum.GetStatusAsync("kyv-1", "comprador", Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStatus("enviado", null, "{}"));

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Updated.Should().BeFalse();
        v.Status.Should().Be(BiometricEstados.EnProceso);
        await _events.DidNotReceive().PublishAsync(Arg.Any<IdentityValidationEvent>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_AlreadyTerminal_SkipsKyverumCall()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed(status: BiometricEstados.Aprobado);

        var (result, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().BeNull();
        result!.Updated.Should().BeFalse();
        await _kyverum.DidNotReceive().GetStatusAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_Missing_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdAsync(_validation, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Reconcile_TenantMismatch_ReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed();

        var (_, error) = await _handler.HandleAsync(_instance, Guid.NewGuid(), _validation, ct);

        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Reconcile_MockProvider_ReturnsNoKyverum()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed(provider: "mock", verificationId: null);

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("no_kyverum");
    }

    [Fact]
    public async Task Reconcile_ProviderTransient_ReturnsUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed();
        _kyverum.GetStatusAsync("kyv-1", "comprador", Arg.Any<CancellationToken>())
            .Returns<KyverumVerifyStatus?>(_ => throw new KyverumVerifyException("down", transient: true));

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("proveedor_no_disponible");
    }

    [Fact]
    public async Task Reconcile_ProviderDefinitive_ReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed();
        _kyverum.GetStatusAsync("kyv-1", "comprador", Arg.Any<CancellationToken>())
            .Returns<KyverumVerifyStatus?>(_ => throw new KyverumVerifyException("bad", transient: false));

        var (_, error) = await _handler.HandleAsync(_instance, _tenant, _validation, ct);

        error.Should().Be("proveedor_error");
    }
}
