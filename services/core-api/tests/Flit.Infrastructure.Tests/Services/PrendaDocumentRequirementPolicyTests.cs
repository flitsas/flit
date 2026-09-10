using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Services;

/// <summary>
/// Default obligatorio; opt-out (document_optional) ⇒ no requerido. Snapshot al CreatedAt.
/// </summary>
public sealed class PrendaDocumentRequirementPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TransitOfficeId = Guid.Parse("aaaaaaaa-0002-4000-8000-000000000002");

    [Fact]
    public async Task SinOptOut_DevuelveTrue_ObligatorioPorDefault()
    {
        var repo = Substitute.For<IOtPrendaDocumentPolicyRepository>();
        repo.IsDocumentOptionalAtAsync(TenantId, TransitOfficeId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var required = await new PrendaDocumentRequirementPolicy(repo).IsRequiredAsync(
            TenantId, TransitOfficeId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        required.Should().BeTrue();
    }

    [Fact]
    public async Task OptOutVigente_DevuelveFalse()
    {
        var repo = Substitute.For<IOtPrendaDocumentPolicyRepository>();
        var createdAt = DateTimeOffset.UtcNow;
        repo.IsDocumentOptionalAtAsync(TenantId, TransitOfficeId, createdAt, Arg.Any<CancellationToken>())
            .Returns(true);

        var required = await new PrendaDocumentRequirementPolicy(repo).IsRequiredAsync(
            TenantId, TransitOfficeId, createdAt, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }

    [Fact]
    public async Task SinTransitOfficeId_DevuelveFalse()
    {
        var repo = Substitute.For<IOtPrendaDocumentPolicyRepository>();

        var required = await new PrendaDocumentRequirementPolicy(repo).IsRequiredAsync(
            TenantId, transitOfficeId: null, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
        await repo.DidNotReceive()
            .IsDocumentOptionalAtAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinTenantId_DevuelveFalse()
    {
        var repo = Substitute.For<IOtPrendaDocumentPolicyRepository>();

        var required = await new PrendaDocumentRequirementPolicy(repo).IsRequiredAsync(
            Guid.Empty, TransitOfficeId, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        required.Should().BeFalse();
    }
}
