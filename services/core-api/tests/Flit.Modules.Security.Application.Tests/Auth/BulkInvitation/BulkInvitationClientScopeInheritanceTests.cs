using Flit.Admin.Application.OtClientScopes;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth.BulkInvitation;

/// <summary>
/// Invitación masiva por Excel en el canal OT con un invitador de alcance de compañías <c>especificos</c>: cada invitado hereda
/// el alcance de quien invita (se guarda ligado a SU invitación), y un alcance vacío rechaza la carga (fail-closed). Un invitador
/// sin restricción (Admin OT sin alcance, Super Admin) no cambia nada.
/// <para>Uso de ejemplo: el Gestor OT restringido a la compañía A carga 2 filas; las dos invitaciones quedan con la compañía A.</para>
/// </summary>
public sealed class BulkInvitationClientScopeInheritanceTests
{
    private static readonly Guid Inviter = Guid.NewGuid();
    private static readonly Guid OtTenant = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid ClientA = Guid.NewGuid();

    private readonly IInvitationScopePolicy _policy = Substitute.For<IInvitationScopePolicy>();
    private readonly IBulkInvitationXlsxParser _parser = Substitute.For<IBulkInvitationXlsxParser>();
    private readonly IInvitationRepository _invitations = Substitute.For<IInvitationRepository>();
    private readonly IOtUserClientScopeRepository _scopes = Substitute.For<IOtUserClientScopeRepository>();
    private readonly IOtUserClientScopeResolver _resolver = Substitute.For<IOtUserClientScopeResolver>();

    private static InvitationScopeContext OtScope() =>
        new(Inviter, OtTenant, ["ot_gestor"], OtTenant, InvitationChannel.Ot);

    private BulkInvitationService Sut()
    {
        _policy.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>())
            .Returns(InvitationScopeDecision.Permitted(
                InvitationTargetKind.TransitOffice,
                [new InvitationRoleOption(RoleId, "Gestor", "Gestor", true, null)]));
        _parser.Parse(Arg.Any<Stream>()).Returns(new BulkInvitationParseResult(
            null,
            [
                new BulkInvitationParsedRow(2, "Ana", "ana@flit.local", "Gestor", false),
                new BulkInvitationParsedRow(3, "Beto", "beto@flit.local", "Gestor", false),
            ]));
        _invitations.RoleExistsInTenantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _invitations.CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>())
            .Returns(_ => Guid.NewGuid());

        _scopes.ResolveTransitOfficeIdOfUserAsync(OtTenant, Inviter, Arg.Any<CancellationToken>()).Returns(Office);

        var tokens = Substitute.For<ISecureTokenGenerator>();
        tokens.Generate().Returns(new GeneratedToken("raw", "hash"));
        var email = Substitute.For<IEmailSender>();
        email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(EmailSendResult.Sent);
        var urls = Substitute.For<INetworkUrlBaseResolver>();
        urls.ForTenantAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("http://localhost/activate");

        var registrations = new Dictionary<Type, object>();
        registrations[typeof(IInvitationRepository)] = _invitations;
        registrations[typeof(IOtUserClientScopeRepository)] = _scopes;
        registrations[typeof(IOtUserClientScopeResolver)] = _resolver;
        registrations[typeof(CreateInvitationHandler)] = new CreateInvitationHandler(
            _invitations, tokens, email, new InvitationOptions(), urls, NullLogger<CreateInvitationHandler>.Instance);
        registrations[typeof(ValidateOtInvitationClientScopeHandler)] = new ValidateOtInvitationClientScopeHandler(_scopes, _resolver);
        var provider = new DictionaryProvider(registrations);

        return new BulkInvitationService(
            _policy, _parser, _invitations, provider, new BulkInvitationOptions(),
            NullLogger<BulkInvitationService>.Instance);
    }

    [Fact]
    public async Task Commit_InvitadorRestringido_CadaInvitadoHeredaSuAlcance()
    {
        _resolver.GetRestrictionForUserAsync(Inviter, Office, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>?)[ClientA]);

        var result = await Sut().ProcessAsync(OtScope(), new MemoryStream([1]), dryRun: false, CancellationToken.None);

        result.Report!.Created.Should().Be(2);
        await _scopes.Received(2).SetInvitationScopeAsync(
            Arg.Any<Guid>(),
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(ClientA)),
            Inviter,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_InvitadorSinRestriccion_NoGuardaAlcance_ComoSiempre()
    {
        _resolver.GetRestrictionForUserAsync(Inviter, Office, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>?)null);

        var result = await Sut().ProcessAsync(OtScope(), new MemoryStream([1]), dryRun: false, CancellationToken.None);

        result.Report!.Created.Should().Be(2);
        await _scopes.DidNotReceiveWithAnyArgs().SetInvitationScopeAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvitadorConAlcanceVacio_RechazaLaCarga_YNoCreaNada(bool dryRun)
    {
        _resolver.GetRestrictionForUserAsync(Inviter, Office, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>?)[]);

        var act = () => Sut().ProcessAsync(OtScope(), new MemoryStream([1]), dryRun, CancellationToken.None);

        await act.Should().ThrowAsync<InvitationScopeDeniedException>()
            .Where(ex => ex.Reason == InvitationScopeReasons.ClientScopeExceedsActor);
        await _invitations.DidNotReceiveWithAnyArgs().CreateAsync(default!, TestContext.Current.CancellationToken);
    }
}

/// <summary>Contenedor mínimo: cada pedido resuelve el mismo objeto registrado (sin DI real, solo el scope factory).</summary>
internal sealed class DictionaryProvider(Dictionary<Type, object> registrations) : IServiceProvider, IServiceScopeFactory, IServiceScope
{
    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IServiceScopeFactory) ? this
        : registrations.GetValueOrDefault(serviceType);

    public IServiceScope CreateScope() => this;

    public void Dispose()
    {
    }
}
