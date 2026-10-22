using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Application.UserManagement.UpdateUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #13810 (Feature #13808, Épica #13516) — el correo que llega del cliente se normaliza (sin espacios, en
/// minúsculas) antes de validar unicidad y de guardarse, al invitar y al editar un usuario.
/// <para>
/// Uso de ejemplo: <c>EmailNormalizer.Normalize(" Juan@X.com ")</c> → <c>"juan@x.com"</c>; un
/// <c>CreateInvitationCommand</c> con <c>" Juan@X.com "</c> crea la invitación con <c>"juan@x.com"</c>.
/// </para>
/// </summary>
public sealed class HU13810NormalizacionCorreoTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    // ── EmailNormalizer ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(" Juan@X.com ", "juan@x.com")]
    [InlineData("JUAN@X.COM", "juan@x.com")]
    [InlineData("juan@x.com", "juan@x.com")]
    [InlineData("\tAna.Perez@Empresa.CO\n", "ana.perez@empresa.co")]
    public void Normalize_devuelve_la_forma_canonica(string entrada, string esperado) =>
        EmailNormalizer.Normalize(entrada).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_de_vacio_o_nulo_es_cadena_vacia(string? entrada) =>
        EmailNormalizer.Normalize(entrada).Should().BeEmpty();

    [Fact]
    public void Normalize_es_idempotente()
    {
        var una = EmailNormalizer.Normalize(" Juan@X.com ");
        EmailNormalizer.Normalize(una).Should().Be(una);
    }

    // ── AC1/AC2 — invitación ────────────────────────────────────────────────────────────────────────

    private static CreateInvitationHandler NuevoHandlerInvitacion(IInvitationRepository repo)
    {
        var tokens = Substitute.For<ISecureTokenGenerator>();
        tokens.Generate().Returns(new GeneratedToken("raw", "hash"));
        var email = Substitute.For<IEmailSender>();
        email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(EmailSendResult.Sent));
        var urls = Substitute.For<INetworkUrlBaseResolver>();
        urls.ForTenantAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(c.ArgAt<string>(1)));
        repo.RoleExistsInTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(true);
        return new CreateInvitationHandler(
            repo, tokens, email, new InvitationOptions { ActivateUrlBase = "http://localhost:3000/invite/activate" },
            urls, Substitute.For<ILogger<CreateInvitationHandler>>());
    }

    [Fact]
    public async Task AC1_La_invitacion_se_crea_y_se_valida_con_el_correo_normalizado()
    {
        var repo = Substitute.For<IInvitationRepository>();
        var handler = NuevoHandlerInvitacion(repo);

        var result = await handler.HandleAsync(
            new CreateInvitationCommand(TenantId, " Juan@X.com ", "Juan", [RoleId], CallerId), CancellationToken.None);

        result.Email.Should().Be("juan@x.com");
        await repo.Received(1).ExistsPendingAsync(TenantId, "juan@x.com", Arg.Any<CancellationToken>());
        await repo.Received(1).UserExistsWithEmailAsync("juan@x.com", Arg.Any<CancellationToken>());
        await repo.Received(1).CreateAsync(Arg.Is<UserInvitationData>(d => d.Email == "juan@x.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_Invitar_un_correo_ya_usado_cambiando_mayusculas_se_bloquea()
    {
        var repo = Substitute.For<IInvitationRepository>();
        repo.UserExistsWithEmailAsync("juan@x.com", Arg.Any<CancellationToken>()).Returns(true);
        var handler = NuevoHandlerInvitacion(repo);

        var act = () => handler.HandleAsync(
            new CreateInvitationCommand(TenantId, "JUAN@x.com", "Juan", [RoleId], CallerId), CancellationToken.None);

        await act.Should().ThrowAsync<UserAlreadyExistsException>();
        await repo.DidNotReceive().CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>());
    }

    // ── AC2 — edición ───────────────────────────────────────────────────────────────────────────────

    private static (UpdateUserHandler Handler, IUserManagementRepository Repo) NuevoHandlerEdicion()
    {
        var repo = Substitute.For<IUserManagementRepository>();
        repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "viejo@flit.co", "Nombre", DeletedAt: null, RowVersion: 1));
        return (new UpdateUserHandler(repo), repo);
    }

    [Fact]
    public async Task AC2_Editar_hacia_un_correo_ocupado_cambiando_mayusculas_se_bloquea()
    {
        var (handler, repo) = NuevoHandlerEdicion();
        repo.FindLiveByEmailAsync("juan@x.com", Arg.Any<CancellationToken>()).Returns(new ExistingUserByEmail(Guid.NewGuid()));

        var act = () => handler.HandleAsync(
            new UpdateUserCommand(TenantId, UserId, null, "JUAN@x.com", 1, CallerId, false), CancellationToken.None);

        await act.Should().ThrowAsync<UserAlreadyExistsException>();
        await repo.DidNotReceive().UpdateProfileAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_Editar_guarda_el_correo_normalizado()
    {
        var (handler, repo) = NuevoHandlerEdicion();

        await handler.HandleAsync(
            new UpdateUserCommand(TenantId, UserId, null, " Nuevo@Flit.CO ", 1, CallerId, false), CancellationToken.None);

        await repo.Received(1).FindLiveByEmailAsync("nuevo@flit.co", Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateProfileAsync(
            UserId, null, "nuevo@flit.co", 1, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }
}
