using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth.AdminResetPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
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
        _handler = new AdminResetPasswordHandler(_repo, _tempGen, _hasher, _email, _auditWriter, _auditContext);
        _tempGen.Generate().Returns("Temp23xy!Kp9Qr");
        _hasher.Hash(Arg.Any<string>()).Returns("hashed-temp");
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
}
