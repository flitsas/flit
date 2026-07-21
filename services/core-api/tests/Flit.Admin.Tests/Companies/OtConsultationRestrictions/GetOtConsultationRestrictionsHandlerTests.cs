using Flit.Admin.Application.Companies.TransitOffices.GetOtConsultationRestrictions;
using Flit.Admin.Domain.Companies.TransitOffices;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.OtConsultationRestrictions;

/// <summary>
/// Tests del caso de uso de lectura de restricciones de consulta por OT (HU #10759, AC1/AC5).
/// </summary>
public sealed class GetOtConsultationRestrictionsHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task MapeaItemsDelRepositorio_AResponse()
    {
        var transitOfficeId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        repository.ListAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OtConsultationRestrictionItem>>(
            [
                new OtConsultationRestrictionItem(transitOfficeId, RestrictedConsultationKinds.Rnmc, false),
                new OtConsultationRestrictionItem(transitOfficeId, RestrictedConsultationKinds.Fines, true),
            ]));
        var handler = new GetOtConsultationRestrictionsHandler(repository);

        var result = await handler.HandleAsync(
            new GetOtConsultationRestrictionsQuery { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(r =>
            r.TransitOfficeId == transitOfficeId
            && r.ConsultationKind == RestrictedConsultationKinds.Rnmc
            && !r.Enabled);
        result.Should().ContainSingle(r =>
            r.TransitOfficeId == transitOfficeId
            && r.ConsultationKind == RestrictedConsultationKinds.Fines
            && r.Enabled);
    }

    [Fact]
    public async Task SinFilas_DevuelveListaVacia()
    {
        var repository = Substitute.For<IOtConsultationRestrictionRepository>();
        repository.ListAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OtConsultationRestrictionItem>>([]));
        var handler = new GetOtConsultationRestrictionsHandler(repository);

        var result = await handler.HandleAsync(
            new GetOtConsultationRestrictionsQuery { TenantId = TenantId }, TestContext.Current.CancellationToken);

        result.Should().BeEmpty();
    }
}
