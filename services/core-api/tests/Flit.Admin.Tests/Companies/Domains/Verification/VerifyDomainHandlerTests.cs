using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains.Verification;

/// <summary>Reloj congelable mínimo para <see cref="VerifyDomainHandlerTests"/> (HU #12425 AC2, cooldown).</summary>
internal sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// HU #12425 AC2, AC7 — <see cref="VerifyDomainHandler"/> con <see cref="FakeDnsTxtResolver"/> (sin BD
/// ni DNS real). Uso de ejemplo:
/// <code>
/// var handler = new VerifyDomainHandler(repo, fakeDns, options);
/// var result = await handler.HandleAsync(tenantId, changedByUserId: null, ct);
/// </code>
/// </summary>
public sealed class VerifyDomainHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static TenantDomain NewDomain(string status, string token, DateTimeOffset? lastCheckedAt = null, DateTimeOffset? graceUntil = null, int checkAttempts = 0) => new()
    {
        TenantId = TenantId,
        Host = "app.movilidadandina.com",
        Status = status,
        VerificationToken = token,
        LastCheckedAt = lastCheckedAt,
        GraceUntil = graceUntil,
        CheckAttempts = checkAttempts,
        StatusChangedAt = Now,
        RowVersion = 1,
    };

    private static DomainVerificationOptions NewOptions() => new() { ManualCooldownSeconds = 30 };

    [Fact]
    public async Task AC7_TxtCoincide_PasaAVerified()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var current = NewDomain(TenantDomainStatus.Pending, "tok-123");
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);
        repo.ApplyCheckOutcomeAsync(
                Arg.Is(TenantId), Arg.Is(TenantDomainStatus.Verified), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Is(0), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(TenantDomainStatus.Verified, "tok-123"));

        var dns = new FakeDnsTxtResolver();
        dns.SetMatch("app.movilidadandina.com", "tok-123");

        var handler = new VerifyDomainHandler(repo, dns, NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VerifyDomainOutcome.Verified);
        result.Domain!.Status.Should().Be(TenantDomainStatus.Verified);
    }

    [Fact]
    public async Task AC7_TxtAusente_PasaAFailedConTxtNotFound()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var current = NewDomain(TenantDomainStatus.Pending, "tok-123");
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(current);
        repo.ApplyCheckOutcomeAsync(
                Arg.Is(TenantId), Arg.Is(TenantDomainStatus.Failed), Arg.Is(DomainStatusReasons.TxtNotFound), Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Is(1), Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(TenantDomainStatus.Failed, "tok-123", checkAttempts: 1));

        var dns = new FakeDnsTxtResolver(); // sin registrar el host ⇒ NotFound

        var handler = new VerifyDomainHandler(repo, dns, NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VerifyDomainOutcome.Failed);
        result.Domain!.Status.Should().Be(TenantDomainStatus.Failed);
    }

    [Fact]
    public async Task AC7_ValorIncorrecto_PasaAFailedConTxtMismatch()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(NewDomain(TenantDomainStatus.Pending, "tok-esperado"));
        repo.ApplyCheckOutcomeAsync(
                Arg.Is(TenantId), Arg.Is(TenantDomainStatus.Failed), Arg.Is(DomainStatusReasons.TxtMismatch), Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Is(1), Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(TenantDomainStatus.Failed, "tok-esperado", checkAttempts: 1));

        var dns = new FakeDnsTxtResolver();
        dns.SetMatch("app.movilidadandina.com", "tok-otro-cliente");

        var handler = new VerifyDomainHandler(repo, dns, NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VerifyDomainOutcome.Failed);
    }

    [Fact]
    public async Task AC7_DesapareceEnActivo_SigueActivoConGracia()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(NewDomain(TenantDomainStatus.Active, "tok-123"));
        repo.ApplyCheckOutcomeAsync(
                Arg.Is(TenantId), Arg.Is(TenantDomainStatus.Active), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Is(0), Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(TenantDomainStatus.Active, "tok-123", graceUntil: Now.AddDays(3)));

        var dns = new FakeDnsTxtResolver(); // TXT desapareció

        var handler = new VerifyDomainHandler(repo, dns, NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Domain!.Status.Should().Be(TenantDomainStatus.Active); // AC4: sigue operando en gracia
        result.Domain!.GraceUntil.Should().NotBeNull();
    }

    [Fact]
    public async Task Cooldown_ComprobacionMuyReciente_Responde429()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(NewDomain(TenantDomainStatus.Pending, "tok-123", lastCheckedAt: Now.AddSeconds(-5)));

        var handler = new VerifyDomainHandler(repo, new FakeDnsTxtResolver(), NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VerifyDomainOutcome.Cooldown);
        result.RetryAfterSeconds.Should().BeGreaterThan(0);
        await repo.DidNotReceive().ApplyCheckOutcomeAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DominioInexistente_Responde404()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);

        var handler = new VerifyDomainHandler(repo, new FakeDnsTxtResolver(), NewOptions(), new FrozenTimeProvider(Now));
        var result = await handler.HandleAsync(TenantId, changedByUserId: null, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(VerifyDomainOutcome.NotFound);
    }
}
