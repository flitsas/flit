using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.Activation;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.Settings;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>Qué le falta a una red para activarse, y que activarla solo ocurra con todo listo.</summary>
public sealed class NetworkActivationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Operator = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Issued = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly TenantProductFlags SoloTramites = new(Tramites: true, Comparendos: false);

    private static TenantDomain Domain(string purpose, string status, bool certificate) => new()
    {
        TenantId = TenantId,
        Purpose = purpose,
        Host = $"{purpose.ToLowerInvariant()}.cliente.com",
        Status = status,
        VerificationToken = "tok-0000000000000001",
        CertificateIssuedAt = certificate ? Issued : null,
        StatusChangedAt = Issued,
        CheckAttempts = 0,
        RowVersion = 1,
    };

    [Fact]
    public void SinDominios_FaltanElHubYCadaProductoHabilitado()
    {
        var status = NetworkActivationStatus.From([], new TenantProductFlags(Tramites: true, Comparendos: true));

        status.Products.Should().Equal("tramites", "comparendos");
        status.Pending.Should().Equal(TenantDomainPurposes.Hub, "tramites", "comparendos");
        status.Ready.Should().BeFalse();
        status.Active.Should().BeFalse();
    }

    [Fact]
    public void ComprobadoSinCertificado_SigueFaltando()
    {
        var status = NetworkActivationStatus.From(
            [Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Verified, certificate: true), Domain("tramites", TenantDomainStatus.Verified, certificate: false)],
            SoloTramites);

        status.Pending.Should().Equal("tramites");
        status.Ready.Should().BeFalse();
    }

    [Fact]
    public void TodoComprobadoYConCertificado_EstaLista()
    {
        var status = NetworkActivationStatus.From(
            [Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Verified, certificate: true), Domain("tramites", TenantDomainStatus.Verified, certificate: true)],
            SoloTramites);

        status.Pending.Should().BeEmpty();
        status.Ready.Should().BeTrue();
        status.Active.Should().BeFalse();
    }

    [Fact]
    public void DominioDeUnProductoApagado_TambienTieneQueEstarListo()
    {
        var status = NetworkActivationStatus.From(
            [
                Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Verified, certificate: true),
                Domain("tramites", TenantDomainStatus.Verified, certificate: true),
                Domain("comparendos", TenantDomainStatus.Failed, certificate: false),
            ],
            SoloTramites);

        status.Pending.Should().Equal("comparendos");
    }

    [Fact]
    public void TodoActivo_NoQuedaNadaPorActivar()
    {
        var status = NetworkActivationStatus.From(
            [Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Active, certificate: true), Domain("tramites", TenantDomainStatus.Active, certificate: true)],
            SoloTramites);

        status.Active.Should().BeTrue();
        status.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task Activar_ConAlgoPendiente_NoEscribeNada()
    {
        var (handler, repo) = NewHandler([Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Verified, certificate: true)]);

        var result = await handler.HandleAsync(TenantId, Operator, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ActivateNetworkOutcome.NotReady);
        result.Status.Pending.Should().Equal("tramites");
        await repo.DidNotReceive().ActivateNetworkAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activar_ConTodoListo_ActivaLaRedConSuAutor()
    {
        var (handler, repo) = NewHandler(
            [Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Verified, certificate: true), Domain("tramites", TenantDomainStatus.Verified, certificate: true)]);
        repo.ActivateNetworkAsync(TenantId, Arg.Any<DateTimeOffset>(), Operator, Arg.Any<CancellationToken>())
            .Returns([Domain(TenantDomainPurposes.Hub, TenantDomainStatus.Active, certificate: true), Domain("tramites", TenantDomainStatus.Active, certificate: true)]);

        var result = await handler.HandleAsync(TenantId, Operator, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ActivateNetworkOutcome.Activated);
        result.Status.Active.Should().BeTrue();
    }

    [Fact]
    public void Instrucciones_PidenRegistroAConLaIpDelBorde()
    {
        var response = TenantDomainResponse.From(Domain("tramites", TenantDomainStatus.Pending, certificate: false), "203.0.113.10");

        response.Purpose.Should().Be("tramites");
        response.Verification.AName.Should().Be("tramites.cliente.com");
        response.Verification.AValue.Should().Be("203.0.113.10");
        response.Verification.TxtName.Should().Be("_flit-verify.tramites.cliente.com");
    }

    private static (ActivateNetworkHandler Handler, ITenantDomainRepository Repo) NewHandler(IReadOnlyList<TenantDomain> domains)
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ListByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(domains);
        var flags = Substitute.For<ITenantProductFlags>();
        flags.GetAsync(TenantId, Arg.Any<CancellationToken>()).Returns(SoloTramites);
        return (new ActivateNetworkHandler(repo, flags), repo);
    }
}
