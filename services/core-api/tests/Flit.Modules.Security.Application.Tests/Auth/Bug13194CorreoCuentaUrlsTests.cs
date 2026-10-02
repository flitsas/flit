using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.ActivateAccount;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// Bug #13194 punto 5 — los correos de cuenta (bienvenida tras activar) no pueden enlazar a
/// <c>dev.flitsas.online</c> en QA/PDN: la URL de login sale de la configuración del ambiente
/// (<c>Invitations:ActivateUrlBase</c>) resuelta por tenant con <see cref="INetworkUrlBaseResolver"/>
/// (marca blanca), y los assets de <c>Notifications:EmailAssets:BaseUrl</c>.
/// <para>
/// Uso de ejemplo:
/// <code>
/// var handler = ActivatorUtilities.CreateInstance&lt;ActivateAccountHandler&gt;(provider);
/// await handler.HandleAsync(new ActivateAccountCommand(token, password), ct);
/// // correo security.welcome-registration con https://qa.flitsas.online/login
/// </code>
/// </para>
/// El handler se construye con <see cref="ActivatorUtilities"/> (como lo hace el contenedor DI en
/// producción): así el test ejercita la composición real de las dependencias registradas.
/// </summary>
public sealed class Bug13194CorreoCuentaUrlsTests
{
    private const string RawToken = "raw-token-13194";
    private const string TokenHash = "hash-13194";
    private const string ValidPassword = "FlitPass1!";
    private const string QaActivateUrlBase = "https://qa.flitsas.online/invite/activate";
    private const string QaAssetsBaseUrl = "https://qa.flitsas.online/email-assets";

    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();
    private readonly ISecureTokenGenerator _tokenGen = Substitute.For<ISecureTokenGenerator>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ITenantNetworkMembership _networkMembership = Substitute.For<ITenantNetworkMembership>();
    private readonly IDomainContextAccessor _domainContext = Substitute.For<IDomainContextAccessor>();
    private EmailMessage? _sent;

    public Bug13194CorreoCuentaUrlsTests()
    {
        _tokenGen.HashToken(RawToken).Returns(TokenHash);
        _hasher.Hash(ValidPassword).Returns("hashed");
        _invitationRepo.FindPendingByTokenHashAsync(TokenHash, Arg.Any<CancellationToken>())
            .Returns(new PendingInvitation(
                Guid.NewGuid(), TenantId, "invited@flit.local", "Usuario QA", [Guid.NewGuid()], Guid.NewGuid()));
        _networkMembership.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);
        _emailSender.SendAsync(Arg.Do<EmailMessage>(m => _sent = m), Arg.Any<CancellationToken>())
            .Returns(EmailSendResult.Sent);
    }

    private ActivateAccountHandler BuildHandler()
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(IInvitationRepository)] = _invitationRepo,
            [typeof(ISecureTokenGenerator)] = _tokenGen,
            [typeof(IUserActivationRepository)] = Substitute.For<IUserActivationRepository>(),
            [typeof(IPasswordHasher)] = _hasher,
            [typeof(IEmailSender)] = _emailSender,
            [typeof(IAdminAuditWriter)] = Substitute.For<IAdminAuditWriter>(),
            [typeof(IAuditContextAccessor)] = NullAuditContextAccessor.Instance,
            [typeof(ITenantNetworkMembership)] = _networkMembership,
            [typeof(IDomainContextAccessor)] = _domainContext,
            [typeof(ILogger<ActivateAccountHandler>)] = NullLogger<ActivateAccountHandler>.Instance,
            [typeof(InvitationOptions)] = new InvitationOptions { ActivateUrlBase = QaActivateUrlBase },
            [typeof(INetworkUrlBaseResolver)] = new NetworkUrlBaseResolver(_networkMembership),
            [typeof(SecurityEmailAssetsOptions)] = new SecurityEmailAssetsOptions { BaseUrl = QaAssetsBaseUrl },
        };
        return ActivatorUtilities.CreateInstance<ActivateAccountHandler>(new DictionaryServiceProvider(services));
    }

    [Fact]
    public async Task Bienvenida_enlaza_login_del_ambiente_configurado_y_nunca_dev()
    {
        await BuildHandler().HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None);

        _sent.Should().NotBeNull();
        _sent!.TemplateKey.Should().Be("security.welcome-registration");
        _sent.HtmlBody.Should().Contain("href=\"https://qa.flitsas.online/login\"");
        _sent.HtmlBody.Should().NotContain("dev.flitsas.online");
    }

    [Fact]
    public async Task Bienvenida_tenant_de_red_marca_blanca_enlaza_login_del_dominio_de_la_red()
    {
        var head = Guid.NewGuid();
        _networkMembership.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new NetworkMembership(head, true, "tramites.marca.co"));
        _domainContext.Kind.Returns(DomainKind.Network);
        _domainContext.Host.Returns("tramites.marca.co");
        _domainContext.HeadTenantId.Returns(head);

        await BuildHandler().HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None);

        _sent.Should().NotBeNull();
        _sent!.HtmlBody.Should().Contain("href=\"https://tramites.marca.co/login\"");
        _sent.HtmlBody.Should().NotContain("qa.flitsas.online/login");
        _sent.HtmlBody.Should().NotContain("dev.flitsas.online");
    }

    [Fact]
    public async Task Bienvenida_toma_los_assets_de_Notifications_EmailAssets()
    {
        await BuildHandler().HandleAsync(new ActivateAccountCommand(RawToken, ValidPassword), CancellationToken.None);

        _sent.Should().NotBeNull();
        _sent!.HtmlBody.Should().Contain($"{QaAssetsBaseUrl}/flit-logo.png");
        _sent.HtmlBody.Should().Contain($"{QaAssetsBaseUrl}/tramite-cambio-estado-header.png");
    }

    [Fact]
    public void Layout_fallback_de_assets_no_apunta_a_dev_y_respeta_la_base_recibida()
    {
        FlitBrandedEmailLayout.DefaultAssetsBaseUrl.Should().NotContain("dev.flitsas.online");

        var html = FlitBrandedEmailLayout.Wrap("TITULO", "<p>x</p>", assetsBaseUrl: QaAssetsBaseUrl);
        html.Should().Contain($"{QaAssetsBaseUrl}/flit-logo.png");
        html.Should().NotContain("dev.flitsas.online");
    }

    [Fact]
    public void Plantilla_bienvenida_no_expone_login_por_defecto()
    {
        // Contrato: la URL de login siempre llega de fuera (configuración resuelta por tenant).
        typeof(WelcomeRegistrationEmailTemplate).GetField("DefaultLoginUrl").Should().BeNull();
    }

    /// <summary>
    /// O8 (review 2) — la base de login sale de una URL ABSOLUTA del frontend: se conserva esquema + host
    /// (+ puerto) y el path pasa a <c>/login</c>.
    /// </summary>
    [Theory]
    [InlineData("https://qa.flitsas.online/invite/activate", "https://qa.flitsas.online/login")]
    [InlineData("  http://localhost:3000/invite/activate  ", "http://localhost:3000/login")]
    public void BuildLoginUrl_con_base_absoluta_conserva_el_host(string baseUrl, string esperado)
    {
        WelcomeRegistrationEmailTemplate.BuildLoginUrl(baseUrl).Should().Be(esperado);
    }

    /// <summary>
    /// O8 (review 2) — una base relativa o inválida es un error de configuración: falla rápido con un
    /// mensaje que nombra la clave, en vez de devolver un <c>/login</c> relativo (enlace muerto en el correo).
    /// </summary>
    [Theory]
    [InlineData("/invite/activate")]
    [InlineData("")]
    [InlineData("qa.flitsas.online/invite/activate")]
    [InlineData("ftp://qa.flitsas.online/invite/activate")]
    public void BuildLoginUrl_con_base_relativa_o_invalida_falla_rapido(string baseUrl)
    {
        var act = () => WelcomeRegistrationEmailTemplate.BuildLoginUrl(baseUrl);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Invitations:ActivateUrlBase*");
    }

    private sealed class DictionaryServiceProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }
}
