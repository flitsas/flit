using Flit.Admin.Application.Companies.Domains.RemoveDomain;
using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// Uso de ejemplo: <c>await new RemoveDomainHandler(repo).HandleAsync(new RemoveDomainCommand { TenantId = id })</c>.
/// HU #12416 AC5 (retiro conserva el dato y deja de resolver).
/// </summary>
public sealed class RemoveDomainHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task AC5_DominioVigente_SeRetira()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var retired = new TenantDomain
        {
            TenantId = TenantId,
            Host = "red.example.com",
            Status = TenantDomainStatus.Active,
            VerificationToken = "tok-0000000000000001",
            StatusChangedAt = DateTimeOffset.UtcNow,
            RowVersion = 3,
            CheckAttempts = 0,
        };
        repo.RetireAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(retired);

        var result = await new RemoveDomainHandler(repo).HandleAsync(
            new RemoveDomainCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RemoveDomainOutcome.Retired);
        result.Domain.Should().BeSameAs(retired);
    }

    [Fact]
    public async Task SinDominioVigente_DevuelveNotFound()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.RetireAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);

        var result = await new RemoveDomainHandler(repo).HandleAsync(
            new RemoveDomainCommand { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RemoveDomainOutcome.NotFound);
    }
}
