using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Application.Auth.CreateInvitation;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #11350 — congela ASUNTO y CUERPO de las 3 plantillas de correo de Seguridad, carácter a
/// carácter, ANTES de que la HU #11351 extraiga su composición a funciones puras.
/// <para>
/// La captura se hace en el <b>punto de envío</b> —el <see cref="EmailMessage"/> que recibe
/// <see cref="IEmailSender"/>— y no llamando a la plantilla: así el golden protege por igual a
/// las que tienen clase propia y a las que hoy componen el HTML dentro del handler, y sobrevive
/// a que la composición cambie de sitio.
/// </para>
/// <para>
/// <b>Si un refactor obliga a editar un <c>.golden.txt</c>, el refactor está mal.</b> Solo un
/// cambio DELIBERADO del correo justifica regenerarlos, y entonces se hace con
/// <c>GOLDEN_UPDATE=1</c> y en un commit aparte que diga por qué.
/// </para>
/// <para>
/// Motivación medida: los 4 handlers de correo de este módulo suman 26 tests y <b>ninguno</b>
/// asevera el asunto; el del reset administrativo —el que transporta la contraseña temporal—
/// solo comprueba que se llamó al emisor. Un refactor que rompiera asunto o enlace pasaba 26/26.
/// </para>
/// </summary>
public sealed class SecurityEmailGoldenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Valores fijos de prueba. Nada de esto es PII real ni una contraseña real (AC3).
    private const string ActivateUrlBase = "https://app.flit.test/invite/activate";
    private const string ResetUrlBase = "https://app.flit.test/password/reset";
    private const string RawToken = "GOLDEN-TOKEN-0000";
    private const string SyntheticPassword = "GoldenTemp0000!";

    [Fact]
    public async Task Invitacion_conserva_asunto_y_cuerpo()
    {
        var repo = Substitute.For<IInvitationRepository>();
        var userManagement = Substitute.For<IUserManagementRepository>();
        var tokenGen = Substitute.For<ISecureTokenGenerator>();
        var email = Substitute.For<IEmailSender>();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        tokenGen.Generate().Returns(new GeneratedToken(RawToken, "hash-golden"));
        repo.CreateAsync(Arg.Any<UserInvitationData>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        repo.RoleExistsInTenantAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        repo.ExistsPendingAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        userManagement.FindByEmailIncludingDeletedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((ExistingUserByEmail?)null);

        var handler = new CreateInvitationHandler(
            repo,
            userManagement,
            tokenGen,
            email,
            new InvitationOptions { ActivateUrlBase = ActivateUrlBase },
            NullLogger<CreateInvitationHandler>.Instance);

        await handler.HandleAsync(
            new CreateInvitationCommand(tenantId, "destinatario@flit.test", "Nombre De Prueba", [roleId], Guid.NewGuid()),
            Ct);

        EmailGolden.Assert(Captured(email), "invitacion");
    }

    [Fact]
    public async Task Recuperacion_de_contrasena_conserva_asunto_y_cuerpo()
    {
        var accounts = Substitute.For<IUserAccountRepository>();
        var tokens = Substitute.For<IPasswordResetTokenRepository>();
        var tokenGen = Substitute.For<ISecureTokenGenerator>();
        var email = Substitute.For<IEmailSender>();

        accounts.FindActiveByEmailAsync("destinatario@flit.test", Arg.Any<CancellationToken>())
            .Returns(new PasswordRecoveryUser(Guid.NewGuid(), "destinatario@flit.test", "Nombre De Prueba"));
        tokenGen.Generate().Returns(new GeneratedToken(RawToken, "hash-golden"));

        // Fijado en el test: el golden protege la PLANTILLA, no el valor configurado del TTL.
        var handler = new ForgotPasswordHandler(
            accounts,
            tokens,
            tokenGen,
            email,
            new PasswordRecoveryOptions { ResetUrlBase = ResetUrlBase, TokenLifetimeMinutes = 30 },
            Substitute.For<IAdminAuditWriter>(),
            NullAuditContextAccessor.Instance);

        await handler.HandleAsync(new ForgotPasswordCommand("destinatario@flit.test"), Ct);

        EmailGolden.Assert(Captured(email), "recuperacion-contrasena");
    }

    [Fact]
    public async Task Reset_administrativo_conserva_asunto_y_cuerpo()
    {
        var accounts = Substitute.For<IUserAccountRepository>();
        var temporaryPassword = Substitute.For<ITemporaryPasswordGenerator>();
        var hasher = Substitute.For<IPasswordHasher>();
        var email = Substitute.For<IEmailSender>();

        accounts.FindActiveTargetByEmailAsync("destinatario@flit.test", Arg.Any<CancellationToken>())
            .Returns(new AdminTargetUser(Guid.NewGuid(), "destinatario@flit.test", "Nombre De Prueba", Guid.NewGuid()));
        temporaryPassword.Generate().Returns(SyntheticPassword);
        hasher.Hash(Arg.Any<string>()).Returns("hash-golden");

        var handler = new AdminResetPasswordHandler(
            accounts,
            temporaryPassword,
            hasher,
            email,
            Substitute.For<IAdminAuditWriter>(),
            NullAuditContextAccessor.Instance);

        await handler.HandleAsync(
            new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "destinatario@flit.test"),
            Ct);

        EmailGolden.Assert(Captured(email), "reset-administrativo");
    }

    /// <summary>Recupera el único <see cref="EmailMessage"/> que el handler entregó al puerto.</summary>
    private static EmailMessage Captured(IEmailSender sender)
    {
        var calls = sender.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IEmailSender.SendAsync)).ToList();
        calls.Should().ContainSingle("el handler debe entregar exactamente un correo al puerto de envío");
        return (EmailMessage)calls[0].GetArguments()[0]!;
    }
}
