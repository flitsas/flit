using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Tests.Identity;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.Persons;

/// <summary>
/// Tests unitarios de <see cref="ReenviarPrevalidacionHandler"/> — HU #10943 (CF-03, Feature #10864).
/// Cobertura de AC2 (reenvío manual sin editar), AC5, AC7, AC9, AC10, AC11.
/// </summary>
public sealed class ReenviarPrevalidacionHandlerTests
{
    private readonly IProcedureInstanceRepository _repo = Substitute.For<IProcedureInstanceRepository>();
    private readonly IKyverumVerifyClient _kyverum = Substitute.For<IKyverumVerifyClient>();
    private readonly FakeWebhookSecretProtector _protector = new();
    private readonly IIdentityValidationEventPublisher _events = Substitute.For<IIdentityValidationEventPublisher>();

    private readonly Guid _tenantId = Guid.NewGuid();

    private ReenviarPrevalidacionHandler BuildHandler(bool isKyverum = false)
    {
        var opts = new BiometricsProviderOptions
        {
            Provider = isKyverum ? BiometricProviders.Kyverum : BiometricProviders.Mock,
        };
        return new ReenviarPrevalidacionHandler(_repo, _kyverum, opts, _protector, _events);
    }

    private (Person Person, ProcedureInstanceBiometricValidation Validation) SeedStandalone(
        string status = BiometricEstados.Enviado,
        string provider = BiometricProviders.Mock,
        int resendCount = 0,
        DateTimeOffset? lastResentAt = null,
        int attempts = 2,
        int reconcilePollCount = 2)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            DocumentType = "CC",
            DocumentNumber = "1020304050",
            FullName = "Ana Ríos",
            Email = "ana.rios@old.com",
            PersonType = PersonTypes.Natural,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PersonId = person.Id,
            Person = person,
            ProcedureInstanceId = null,
            Name = person.FullName,
            DocumentType = person.DocumentType,
            DocumentNumber = person.DocumentNumber,
            Email = person.Email,
            Status = status,
            Provider = provider,
            TokenHash = "old-hash",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1), // vencido, escenario típico de reenvío
            Attempts = attempts,
            ReconcilePollCount = reconcilePollCount,
            ResendCount = resendCount,
            LastResentAt = lastResentAt,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        return (person, validation);
    }

    private void StubKyverumOk(string verificationId = "kyv_new", string captureUrl = "https://capture.example.com/new")
    {
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .Returns(new KyverumVerifyStartResult(
                verificationId, captureUrl, "whsec_new", "pending",
                $"{{\"id\":\"{verificationId}\"}}", DateTimeOffset.UtcNow.AddHours(24)));
    }

    // ── AC2 — reenvío manual sin editar ──────────────────────────────────────────

    [Fact]
    public async Task AC2_ManualResend_SendsNewLinkToSameEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone();
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        validation.Email.Should().Be("ana.rios@old.com");
    }

    [Fact]
    public async Task AC2_ManualResend_ResetsAttemptsAndReconcilePollCountToZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(attempts: 3, reconcilePollCount: 2);
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, ct);

        validation.Attempts.Should().Be(0);
        validation.ReconcilePollCount.Should().Be(0);
    }

    [Fact]
    public async Task AC2_ManualResend_ResendCountGoesFromZeroToOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone();
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, ct);

        validation.ResendCount.Should().Be(1);
    }

    [Fact]
    public async Task AC2_ManualResend_Mock_StatusBecomesEnviado()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(status: BiometricEstados.Expirado);
        var handler = BuildHandler(isKyverum: false);

        await handler.HandleAsync(_tenantId, validation.Id, ct);

        validation.Status.Should().Be(BiometricEstados.Enviado);
    }

    [Fact]
    public async Task AC2_ManualResend_Kyverum_StatusBecomesEnProceso()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(status: BiometricEstados.Expirado, provider: BiometricProviders.Kyverum);
        StubKyverumOk();
        var handler = BuildHandler(isKyverum: true);

        await handler.HandleAsync(_tenantId, validation.Id, ct);

        validation.Status.Should().Be(BiometricEstados.EnProceso);
    }

    // ── AC5 — validación de un trámite no es editable ────────────────────────────

    [Fact]
    public async Task AC5_ValidationBoundToProcedure_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        var validation = new ProcedureInstanceBiometricValidation
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProcedureInstanceId = Guid.NewGuid(),
            Status = BiometricEstados.Enviado,
        };
        _repo.GetBiometricByIdWithPersonAsync(validation.Id, _tenantId, Arg.Any<CancellationToken>())
            .Returns(validation);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("no_editable");
        result.Should().BeNull();
    }

    // ── AC7 — identidad aprobada bloquea el reenvío ──────────────────────────────

    [Fact]
    public async Task AC7_ApprovedIdentity_Blocks()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(status: BiometricEstados.Aprobado);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("identidad_aprobada");
        result.Should().BeNull();
    }

    // ── AC9 — cooldown entre reenvíos ─────────────────────────────────────────────

    [Fact]
    public async Task AC9_Cooldown_BlocksResend_WhenResentTwoMinutesAgo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(lastResentAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var handler = BuildHandler();

        var (result, error, cooldownMinutos) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("reenvio_en_cooldown");
        result.Should().BeNull();
        cooldownMinutos.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(BiometricRules.ReenvioCooldownMinutos);
    }

    [Fact]
    public async Task Cooldown_DoesNotBlock_WhenElapsedMoreThanFiveMinutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(lastResentAt: DateTimeOffset.UtcNow.AddMinutes(-30));
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
    }

    // ── AC10 — tope de reenvíos agotado ──────────────────────────────────────────

    [Fact]
    public async Task AC10_MaxResendsReached_Returns429_AndDoesNotConsumeProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(resendCount: BiometricRules.MaxReenvios, provider: BiometricProviders.Kyverum);
        var handler = BuildHandler(isKyverum: true);

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("tope_reenvios");
        result.Should().BeNull();
        await _kyverum.DidNotReceive().StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>());
    }

    // ── AC11 — falla transitoria del proveedor al reenviar ───────────────────────

    [Fact]
    public async Task AC11_KyverumTransientFailure_Returns202_LeavesPendienteEnvio_AndConsumesQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(status: BiometricEstados.Expirado, provider: BiometricProviders.Kyverum);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("caído", transient: true));
        var handler = BuildHandler(isKyverum: true);

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().BeNull();
        result!.Queued.Should().BeTrue();
        validation.Status.Should().Be(BiometricEstados.PendienteEnvio);
        validation.ResendCount.Should().Be(1, "el reenvío ya consumió cupo del tope aunque no se completó el envío");
    }

    [Fact]
    public async Task Kyverum_DefinitiveFailure_ReturnsProveedorError_WithoutConsumingQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone(provider: BiometricProviders.Kyverum);
        _kyverum.StartVerificationAsync(Arg.Any<KyverumVerifyStartRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KyverumVerifyException("rechazado", transient: false));
        var handler = BuildHandler(isKyverum: true);

        var (result, error, _) = await handler.HandleAsync(_tenantId, validation.Id, ct);

        error.Should().Be("proveedor_error");
        result.Should().BeNull();
        validation.ResendCount.Should().Be(0);
    }

    [Fact]
    public async Task NotFound_WhenValidationDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        _repo.GetBiometricByIdWithPersonAsync(Arg.Any<Guid>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns((ProcedureInstanceBiometricValidation?)null);
        var handler = BuildHandler();

        var (result, error, _) = await handler.HandleAsync(_tenantId, Guid.NewGuid(), ct);

        error.Should().Be("not_found");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesCalledOnce_AfterSuccessfulResend()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, validation) = SeedStandalone();
        var handler = BuildHandler();

        await handler.HandleAsync(_tenantId, validation.Id, ct);

        await _repo.Received(1).SaveChangesAsync(ct);
    }
}
