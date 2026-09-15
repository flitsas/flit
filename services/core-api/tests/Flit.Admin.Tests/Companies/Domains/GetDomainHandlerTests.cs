using Flit.Admin.Application.Companies.Domains.GetDomain;
using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>Uso de ejemplo: <c>await new GetDomainHandler(repo).HandleAsync(tenantId)</c> → <c>null</c> si no hay dominio (HU #12416 AC1).</summary>
public sealed class GetDomainHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public async Task SinDominio_DevuelveNull()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);

        var result = await new GetDomainHandler(repo).HandleAsync(TenantId, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ConDominio_DevuelveLaProyeccionDelRepositorio()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var domain = new TenantDomain
        {
            TenantId = TenantId,
            Host = "red.example.com",
            Status = TenantDomainStatus.Pending,
            VerificationToken = "tok-0000000000000001",
            StatusChangedAt = DateTimeOffset.UtcNow,
            RowVersion = 0,
        };
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(domain);

        var result = await new GetDomainHandler(repo).HandleAsync(TenantId, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(domain);
    }
}
