using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Tramites.Application.Notifications;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>Marca FLIT/Renting del correo de asignación de placa = canal del tenant.</summary>
public sealed class PlateAssignmentBrandResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CanalTenantApi_ResuelveMarcaRenting()
    {
        var channels = Substitute.For<INotificationChannelResolver>();
        channels.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(NotificationChannel.TenantApi);

        var sut = new PlateAssignmentBrandResolver(channels);
        var brand = await sut.ResolveForClientTenantAsync(Guid.NewGuid(), Ct);

        brand.Should().Be(PlateAssignmentEmailBrand.Renting);
    }

    [Theory]
    [InlineData(NotificationChannel.FlitSmtp)]
    public async Task CanalFlitSmtp_ResuelveMarcaFlit(NotificationChannel channel)
    {
        var channels = Substitute.For<INotificationChannelResolver>();
        channels.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(channel);

        var sut = new PlateAssignmentBrandResolver(channels);
        var brand = await sut.ResolveForClientTenantAsync(Guid.NewGuid(), Ct);

        brand.Should().Be(PlateAssignmentEmailBrand.Flit);
    }

    [Fact]
    public void BrandFromChannel_MapeaTenantApiARentingYRestoAFlit()
    {
        PlateAssignmentBrandResolver.BrandFromChannel(NotificationChannel.TenantApi)
            .Should().Be(PlateAssignmentEmailBrand.Renting);
        PlateAssignmentBrandResolver.BrandFromChannel(NotificationChannel.FlitSmtp)
            .Should().Be(PlateAssignmentEmailBrand.Flit);
    }
}
