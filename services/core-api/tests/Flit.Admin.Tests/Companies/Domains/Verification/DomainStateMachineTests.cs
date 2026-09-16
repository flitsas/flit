using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains.Verification;

/// <summary>
/// HU #12425 AC2, AC4, AC7 — <see cref="DomainStateMachine"/> pura (sin BD ni DNS). Uso de ejemplo:
/// <code>
/// var decision = DomainStateMachine.DecideCheck(new DomainCheckContext("pending", null, 0, DnsCheckOutcome.Matches, now), options);
/// </code>
/// </summary>
public sealed class DomainStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static DomainVerificationOptions NewOptions() => new()
    {
        InitialRetryInterval = TimeSpan.FromMinutes(5),
        MaxRetryInterval = TimeSpan.FromHours(6),
        BackoffFactor = 2.0,
        RevalidationInterval = TimeSpan.FromHours(24),
        GracePeriod = TimeSpan.FromDays(3),
    };

    [Fact]
    public void AC7_TxtCoincide_DesdePending_PasaAVerified()
    {
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Pending, null, 0, DnsCheckOutcome.Matches, Now), NewOptions());

        decision.NewStatus.Should().Be(TenantDomainStatus.Verified);
        decision.VerifiedAt.Should().Be(Now);
        decision.StatusReason.Should().BeNull();
        decision.NextCheckAt.Should().BeNull(); // verified no se reprograma en el job (#12426 la activa)
    }

    [Fact]
    public void AC7_TxtAusente_DesdePending_PasaAFailedConMotivoYReintentoCreciente()
    {
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Pending, null, 0, DnsCheckOutcome.NotFound, Now), NewOptions());

        decision.NewStatus.Should().Be(TenantDomainStatus.Failed);
        decision.StatusReason.Should().Be(DomainStatusReasons.TxtNotFound);
        decision.CheckAttempts.Should().Be(1);
        decision.NextCheckAt.Should().Be(Now + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void AC7_ValorIncorrecto_DesdePending_PasaAFailedConMismatch()
    {
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Pending, null, 0, DnsCheckOutcome.Mismatch, Now), NewOptions());

        decision.NewStatus.Should().Be(TenantDomainStatus.Failed);
        decision.StatusReason.Should().Be(DomainStatusReasons.TxtMismatch);
    }

    [Fact]
    public void AC2_ErrorDns_DesdeFailed_ReintentaConBackoffCreciente()
    {
        var options = NewOptions();
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Failed, null, 2, DnsCheckOutcome.Error, Now), options);

        decision.NewStatus.Should().Be(TenantDomainStatus.Failed);
        decision.StatusReason.Should().Be(DomainStatusReasons.DnsError);
        decision.CheckAttempts.Should().Be(3);
        decision.NextCheckAt.Should().Be(Now + DomainStateMachine.Backoff(3, options));
        (decision.NextCheckAt! - Now).Should().BeGreaterThan(options.InitialRetryInterval);
    }

    [Fact]
    public void Backoff_NuncaExcedeElTope()
    {
        var options = NewOptions();

        var backoff = DomainStateMachine.Backoff(50, options);

        backoff.Should().Be(options.MaxRetryInterval);
    }

    [Fact]
    public void AC4_TxtDesapareceEnActivo_EntraEnGraciaYSigueActivo()
    {
        var options = NewOptions();
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Active, null, 0, DnsCheckOutcome.NotFound, Now), options);

        decision.NewStatus.Should().Be(TenantDomainStatus.Active); // "durante la gracia sigue operando"
        decision.GraceUntil.Should().Be(Now + options.GracePeriod);
        decision.RaiseGraceAlert.Should().BeTrue();
    }

    [Fact]
    public void AC4_TxtVuelveDuranteLaGracia_LimpiaLaGracia()
    {
        var graceUntil = Now.AddDays(1);
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Active, graceUntil, 0, DnsCheckOutcome.Matches, Now), NewOptions());

        decision.NewStatus.Should().Be(TenantDomainStatus.Active);
        decision.GraceUntil.Should().BeNull();
        decision.RaiseGraceAlert.Should().BeFalse();
    }

    [Fact]
    public void AC4_GraciaVencidaSinTxt_PasaAFailed()
    {
        var graceUntil = Now.AddMinutes(-1); // ya venció
        var decision = DomainStateMachine.DecideCheck(
            new DomainCheckContext(TenantDomainStatus.Active, graceUntil, 0, DnsCheckOutcome.NotFound, Now), NewOptions());

        decision.NewStatus.Should().Be(TenantDomainStatus.Failed);
        decision.StatusReason.Should().Be(DomainStatusReasons.TxtNotFound);
        decision.GraceUntil.Should().BeNull();
    }

    [Fact]
    public void AC3_Certificado_SoloVerifiedPasaAActive()
    {
        var decision = DomainStateMachine.DecideCertificate(TenantDomainStatus.Verified, Now);

        decision.NewStatus.Should().Be(TenantDomainStatus.Active);
        decision.ActivatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(TenantDomainStatus.Pending)]
    [InlineData(TenantDomainStatus.Failed)]
    [InlineData(TenantDomainStatus.Active)]
    public void AC3_Certificado_EnCualquierOtroEstado_NoCambiaDeEstado(string status)
    {
        var decision = DomainStateMachine.DecideCertificate(status, Now);

        decision.NewStatus.Should().Be(status);
        decision.ActivatedAt.Should().BeNull();
    }
}
