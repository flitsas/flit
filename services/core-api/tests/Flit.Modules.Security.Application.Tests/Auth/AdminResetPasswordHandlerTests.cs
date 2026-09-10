using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

public sealed class AdminResetPasswordHandlerTests
{
    private readonly IUserAccountRepository _repo = Substitute.For<IUserAccountRepository>();
    private readonly ITemporaryPasswordGenerator _tempGen = Substitute.For<ITemporaryPasswordGenerator>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly AdminResetPasswordHandler _handler;

    public AdminResetPasswordHandlerTests()
    {
        _handler = new AdminResetPasswordHandler(
            _repo, _tempGen, _hasher, _email, _auditWriter, _auditContext,
            NullLogger<AdminResetPasswordHandler>.Instance);
        _tempGen.Generate().Returns("Temp23xy!Kp9Qr");
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-temp");
        // HU #11358 — por defecto el sender simula éxito (antes lo hacía implícitamente un Task
        // no configurado).
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Sent));
    }

    private AdminTargetUser ArrangeTarget(Guid tenantId)
    {
        var target = new AdminTargetUser(Guid.NewGuid(), "user@flit.local", "Usuario", tenantId);
        _repo.FindActiveTargetByEmailAsync("user@flit.local", Arg.Any<CancellationToken>()).Returns(target);
        return target;
    }

    [Fact]
    public async Task HandleAsync_SuperAdmin_ResetsWithMustChangeAndSendsEmail()
    {
        var target = ArrangeTarget(Guid.NewGuid());
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "user@flit.local");

        await _handler.HandleAsync(command, CancellationToken.None);

        await _repo.Received(1).UpdatePasswordHashAsync(
            target.UserId, "hashed-temp", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    // HU #11358 AC1 — el tenant del usuario objetivo (no el del caller) viaja explícito en la
    // solicitud de envío.
    [Fact]
    public async Task HandleAsync_SendsMessageWithTargetTenantId()
    {
        var target = ArrangeTarget(Guid.NewGuid());
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "user@flit.local");

        await _handler.HandleAsync(command, CancellationToken.None);

        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.TenantId == target.TenantId), Arg.Any<CancellationToken>());
    }

    // HU #11358 AC3 — un fallo tipado del transporte NO se propaga como excepción: la contraseña
    // ya quedó actualizada y el handler termina con normalidad.
    [Fact]
    public async Task HandleAsync_EmailTransportFails_DoesNotThrowAndPasswordStaysUpdated()
    {
        var target = ArrangeTarget(Guid.NewGuid());
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "user@flit.local");
        _email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailSendResult.Failed(EmailSendOutcome.AuthenticationFailed)));

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _repo.Received(1).UpdatePasswordHashAsync(
            target.UserId, "hashed-temp", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TenantAdminWithPermissionSameTenant_Resets()
    {
        var tenant = Guid.NewGuid();
        var target = ArrangeTarget(tenant);
        var command = new AdminResetPasswordCommand(
            tenant, "company_admin", [AdminResetPasswordHandler.ResetPermission], "user@flit.local");

        await _handler.HandleAsync(command, CancellationToken.None);

        await _repo.Received(1).UpdatePasswordHashAsync(
            target.UserId, "hashed-temp", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminCompanyRoleSameTenant_ResetsWithoutPermissionClaim()
    {
        var tenant = Guid.NewGuid();
        var target = ArrangeTarget(tenant);
        var command = new AdminResetPasswordCommand(
            tenant, AdminResetPasswordHandler.AdminCompanyRole, [], "user@flit.local");

        await _handler.HandleAsync(command, CancellationToken.None);

        await _repo.Received(1).UpdatePasswordHashAsync(
            target.UserId, "hashed-temp", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AdminCompanyRoleDifferentTenant_ThrowsScope()
    {
        ArrangeTarget(Guid.NewGuid());
        var command = new AdminResetPasswordCommand(
            Guid.NewGuid(), AdminResetPasswordHandler.AdminCompanyRole, [], "user@flit.local");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<AdminScopeException>();
    }

    [Fact]
    public async Task HandleAsync_PermissionButDifferentTenant_ThrowsScope()
    {
        ArrangeTarget(Guid.NewGuid());
        var command = new AdminResetPasswordCommand(
            Guid.NewGuid(), "company_admin", [AdminResetPasswordHandler.ResetPermission], "user@flit.local");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<AdminScopeException>();
        await _repo.DidNotReceiveWithAnyArgs().UpdatePasswordHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SameTenantButNoPermission_ThrowsScope()
    {
        var tenant = Guid.NewGuid();
        ArrangeTarget(tenant);
        var command = new AdminResetPasswordCommand(tenant, "operador", [], "user@flit.local");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<AdminScopeException>();
    }

    [Fact]
    public async Task HandleAsync_TargetNotFound_ThrowsNotFound()
    {
        _repo.FindActiveTargetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AdminTargetUser?)null);
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "ghost@flit.local");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();
    }

    // HU #11553 AC3 — la PRIMERA temporal generada coincide con la vigente del usuario objetivo:
    // el admin no elige la contraseña, así que la colisión se resuelve regenerando en silencio
    // (sin excepción, sin auditar fallo) y el reset se completa con la segunda temporal.
    [Fact]
    public async Task HandleAsync_FirstGeneratedTemporaryPasswordCollides_RegeneratesAndCompletesReset()
    {
        var target = ArrangeTarget(Guid.NewGuid());
        _repo.GetPasswordHashAsync(target.UserId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _tempGen.Generate().Returns("Temp23xy!Kp9Qr", "OtraTemp99!Zz");
        _hasher.Verify("Temp23xy!Kp9Qr", "current-hash").Returns(true);
        _hasher.Verify("OtraTemp99!Zz", "current-hash").Returns(false);
        _hasher.Hash("OtraTemp99!Zz").Returns("hashed-second-temp");
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "user@flit.local");

        await _handler.HandleAsync(command, CancellationToken.None);

        await _repo.Received(1).UpdatePasswordHashAsync(
            target.UserId, "hashed-second-temp", Arg.Any<DateTimeOffset>(), true, Arg.Any<CancellationToken>());
        await _email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        // Camino feliz (colisionó una vez y se regeneró) NO se audita como fallo.
        await _auditWriter.DidNotReceive().WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.ErrorCode == "password_reused"), Arg.Any<CancellationToken>());
    }

    // HU #11553 AC3 (tope agotado) — el generador SIEMPRE colisiona con la vigente dentro del
    // tope de reintentos → fallo real del generador, no accionable por el admin: ahí sí
    // PasswordReusedException, sin persistir ni enviar correo, y SÍ se audita.
    [Fact]
    public async Task HandleAsync_TemporaryPasswordAlwaysCollides_ThrowsPasswordReusedAfterExhaustingRetries()
    {
        var target = ArrangeTarget(Guid.NewGuid());
        _repo.GetPasswordHashAsync(target.UserId, Arg.Any<CancellationToken>()).Returns("current-hash");
        _tempGen.Generate().Returns("SiempreIgual1!");
        _hasher.Verify("SiempreIgual1!", "current-hash").Returns(true);
        var command = new AdminResetPasswordCommand(Guid.NewGuid(), "SuperAdmin", [], "user@flit.local");

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<PasswordReusedException>();

        _tempGen.Received(5).Generate();
        await _repo.DidNotReceiveWithAnyArgs().UpdatePasswordHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
        await _auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.ErrorCode == "password_reused"), Arg.Any<CancellationToken>());
    }
}
