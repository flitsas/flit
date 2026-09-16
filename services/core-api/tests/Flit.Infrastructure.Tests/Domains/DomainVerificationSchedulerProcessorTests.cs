using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Domains;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Domains;

/// <summary>
/// HU #12425 AC2, AC6, AC7 — <see cref="DomainVerificationSchedulerProcessor.TickAsync"/> sin BD real
/// (repositorio sustituido; el ciclo contra PostgreSQL real lo cubre
/// <c>Flit.Integration.Tests.Domains.TenantDomainLifecycleTests</c>). Uso de ejemplo:
/// <code>
/// var processed = await processor.TickAsync(ct); // 0 si no hay dominios vencidos (AC6)
/// </code>
/// </summary>
public sealed class DomainVerificationSchedulerProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DomainVerificationSchedulerProcessor NewProcessor(
        ITenantDomainRepository repository, Flit.Admin.Application.Companies.Domains.Verification.IDnsTxtResolver dns, out IServiceScopeFactory scopeFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(dns);
        var provider = services.BuildServiceProvider();
        scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new DomainVerificationOptions { BatchSize = 10, PollInterval = TimeSpan.FromSeconds(30) };
        return new DomainVerificationSchedulerProcessor(
            scopeFactory, options, NullLogger<DomainVerificationSchedulerProcessor>.Instance, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task AC6_SinDominiosVencidos_NoHaceNada()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ClaimDueForCheckAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DomainCheckClaim>)[]);
        var processor = NewProcessor(repo, Substitute.For<Flit.Admin.Application.Companies.Domains.Verification.IDnsTxtResolver>(), out _);

        var processed = await processor.TickAsync(Ct);

        processed.Should().Be(0);
        await repo.DidNotReceive().GetByTenantIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await repo.DidNotReceive().ApplyCheckOutcomeAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<int>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_UnDominioVencido_SeComprueebaYSeAplicaElResultado()
    {
        var tenantId = Guid.NewGuid();
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ClaimDueForCheckAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DomainCheckClaim>)[new DomainCheckClaim(tenantId, "app.example.com", TenantDomainStatus.Pending, null, 0)]);
        repo.GetByTenantIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new TenantDomain
        {
            TenantId = tenantId,
            Host = "app.example.com",
            Status = TenantDomainStatus.Pending,
            VerificationToken = "tok-abc",
            StatusChangedAt = Now,
            CheckAttempts = 0,
            RowVersion = 1,
        });

        var dns = new LocalFakeDnsTxtResolver();
        dns.SetMatch("app.example.com", "tok-abc");

        var processor = NewProcessor(repo, dns, out _);

        var processed = await processor.TickAsync(Ct);

        processed.Should().Be(1);
        await repo.Received(1).ApplyCheckOutcomeAsync(
            Arg.Is(tenantId), Arg.Is(TenantDomainStatus.Verified), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Is(0), Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Is(DomainVerificationSchedulerProcessor.JobActor), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC6_FalloEnUnaFila_NoDetieneElLote()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ClaimDueForCheckAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DomainCheckClaim>)
            [
                new DomainCheckClaim(tenant1, "roto.example.com", TenantDomainStatus.Pending, null, 0),
                new DomainCheckClaim(tenant2, "bien.example.com", TenantDomainStatus.Pending, null, 0),
            ]);
        repo.GetByTenantIdAsync(tenant1, Arg.Any<CancellationToken>()).Returns<TenantDomain?>(_ => throw new InvalidOperationException("BD caída"));
        repo.GetByTenantIdAsync(tenant2, Arg.Any<CancellationToken>()).Returns(new TenantDomain
        {
            TenantId = tenant2,
            Host = "bien.example.com",
            Status = TenantDomainStatus.Pending,
            VerificationToken = "tok-xyz",
            StatusChangedAt = Now,
            CheckAttempts = 0,
            RowVersion = 1,
        });

        var dns = new LocalFakeDnsTxtResolver();
        dns.SetMatch("bien.example.com", "tok-xyz");

        var processor = NewProcessor(repo, dns, out _);

        var processed = await processor.TickAsync(Ct);

        processed.Should().Be(1); // solo la fila sana cuenta; la rota no tumba el ciclo
        await repo.Received(1).ApplyCheckOutcomeAsync(
            Arg.Is(tenant2), Arg.Is(TenantDomainStatus.Verified), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Is(0), Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Is(DomainVerificationSchedulerProcessor.JobActor), Arg.Any<CancellationToken>());
    }
}

/// <summary>Reloj fijo mínimo para estos tests.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Resolutor DNS simulado local a este proyecto de tests (HU #12425 AC7) — mismo contrato que <c>Flit.Admin.Tests</c>, sin cruzar ensamblados de test.</summary>
internal sealed class LocalFakeDnsTxtResolver : Flit.Admin.Application.Companies.Domains.Verification.IDnsTxtResolver
{
    private readonly Dictionary<string, Flit.Admin.Application.Companies.Domains.Verification.DnsTxtLookupResult> _byHost = new(StringComparer.OrdinalIgnoreCase);

    public void SetMatch(string host, string token) =>
        _byHost[host] = Flit.Admin.Application.Companies.Domains.Verification.DnsTxtLookupResult.Success([token]);

    public Task<Flit.Admin.Application.Companies.Domains.Verification.DnsTxtLookupResult> LookupAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byHost.TryGetValue(host, out var result)
            ? result
            : Flit.Admin.Application.Companies.Domains.Verification.DnsTxtLookupResult.NotFound());
}
