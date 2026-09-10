using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Routing;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Routing;

/// <summary>
/// HU #11466 — resolutor compartido de canal. Congela los defaults que el worker y el router
/// deben compartir (sin tenant / sin política ⇒ FlitSmtp).
/// </summary>
public sealed class NotificationChannelResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SinTenantResoluble_ElCanalEsFlitSmtp()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        var resolver = new NotificationChannelResolver(repo);

        var channel = await resolver.ResolveAsync(null, Ct);

        channel.Should().Be(NotificationChannel.FlitSmtp);
        await repo.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinPoliticaOperativa_ElCanalEsFlitSmtp()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns((TenantSettings?)null);
        var resolver = new NotificationChannelResolver(repo);

        var channel = await resolver.ResolveAsync(TenantId, Ct);

        channel.Should().Be(NotificationChannel.FlitSmtp);
    }

    [Fact]
    public async Task TenantConCanalApi_MantieneTenantApi()
    {
        var repo = Substitute.For<ITenantSettingsRepository>();
        repo.GetAsync(TenantId, Ct).Returns(new TenantSettings
        {
            TenantId = TenantId,
            AllowInitialRegistration = true,
            AllowMiscNewVehicles = true,
            OnlyOwnVehicles = false,
            SignatureVaultEnabled = false,
            NotificationChannel = NotificationChannel.TenantApi,
            NotificationTarget = NotificationTarget.Radicador,
            PaymentMethods = [],
        });
        var resolver = new NotificationChannelResolver(repo);

        var channel = await resolver.ResolveAsync(TenantId, Ct);

        channel.Should().Be(NotificationChannel.TenantApi);
    }
}
