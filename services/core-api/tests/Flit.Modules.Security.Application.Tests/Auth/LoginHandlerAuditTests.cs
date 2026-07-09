using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #10678 (RNF01, ADR-0024 extendido): auditoría de autenticación desde
/// <see cref="LoginHandler"/>. Cubre AC4 (login fallido sin tenant resoluble, la auditoría
/// nunca rompe el flujo), y AC6 (sin contraseñas ni email en el rastro).
/// </summary>
public sealed class LoginHandlerAuditTests
{
    private readonly IAuthUserRepository _repository = Substitute.For<IAuthUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IJwtTokenIssuer _jwtTokenIssuer = Substitute.For<IJwtTokenIssuer>();
    private readonly IAdminAuditWriter _auditWriter = Substitute.For<IAdminAuditWriter>();
    private readonly IAuditContextAccessor _auditContext = NullAuditContextAccessor.Instance;
    private readonly LoginHandler _handler;

    public LoginHandlerAuditTests()
    {
        _handler = new LoginHandler(_repository, _passwordHasher, _jwtTokenIssuer, _auditWriter, _auditContext);
    }

    // ── AC4 — login fallido con email inexistente: se audita sin tenant y sin lanzar por la auditoría ──

    [Fact]
    public async Task HandleAsync_UnknownEmail_AuditsLoginFailedWithNullTenantAndDoesNotThrowFromAudit()
    {
        _repository.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((UserAuthSnapshot?)null);

        var act = () => _handler.HandleAsync(
            new LoginCommand("ghost@flit.local", "WhateverPass1!"), CancellationToken.None);

        // La auditoría es best-effort: aunque el usuario no exista, se debe registrar sin romper
        // el flujo (la excepción de negocio esperada es InvalidCredentialsException, no una del
        // pipeline de auditoría).
        await act.Should().ThrowAsync<InvalidCredentialsException>();

        await _auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.TenantId == null
                && e.ActorUserId == null
                && e.Operation == AuditVocabulary.Operations.LoginFailed
                && e.Result == AuditVocabulary.Results.Failure
                && e.Module == AuditVocabulary.Modules.Authentication),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidCredentials_AuditsLoginSuccessWithActorAndTargetEqualToUser()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        _repository.FindByEmailAsync("demo@flit.local", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = userId,
                Email = "demo@flit.local",
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                TenantName = "Acme Renting SAS",
                TenantTaxId = "900123456-7",
                EntityType = "COMPANY",
                ActiveRoles = [new UserRoleSnapshot(roleId, "demo_admin")],
                PermissionSlugs = ["auth.me.read"],
            });
        _passwordHasher.Verify("DemoPass1!", "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        await _handler.HandleAsync(new LoginCommand("demo@flit.local", "DemoPass1!"), CancellationToken.None);

        await _auditWriter.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e =>
                e.TenantId == tenantId
                && e.ActorUserId == userId
                && e.TargetEntityId == userId
                && e.Operation == AuditVocabulary.Operations.Login
                && e.Result == AuditVocabulary.Results.Success),
            Arg.Any<CancellationToken>());
    }

    // ── AC6 — sin PII: ni la contraseña ni el email viajan en el rastro de auditoría ────

    [Fact]
    public async Task HandleAsync_InvalidPassword_AuditEntryNeverContainsPasswordOrEmail()
    {
        const string password = "SuperSecret123!";
        const string email = "demo@flit.local";
        _repository.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = email,
                Status = "active",
                PasswordHash = "hash",
                TenantId = Guid.NewGuid(),
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
            });
        _passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var received = new List<AdminAuditEntry>();
        _auditWriter.WriteAsync(Arg.Do<AdminAuditEntry>(received.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var act = () => _handler.HandleAsync(new LoginCommand(email, password), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidCredentialsException>();

        received.Should().ContainSingle();
        var entry = received.Single();

        // Ningún campo string de la entrada de auditoría debe contener la contraseña o el email
        // usados en el intento de login.
        AssertNoPii(entry, password, email);
    }

    [Fact]
    public async Task HandleAsync_ValidLogin_AuditEntryNeverContainsPasswordOrEmail()
    {
        const string password = "DemoPass1!";
        const string email = "demo@flit.local";
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _repository.FindByEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = userId,
                Email = email,
                Status = "active",
                PasswordHash = "hash",
                TenantId = tenantId,
                TenantName = "Acme Renting SAS",
                TenantTaxId = "900123456-7",
                EntityType = "COMPANY",
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
            });
        _passwordHasher.Verify(password, "hash").Returns(true);
        _jwtTokenIssuer.IssueToken(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<UserRoleSnapshot>>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new IssuedAccessToken { Token = "jwt-token", ExpiresInSeconds = 43200 });

        var received = new List<AdminAuditEntry>();
        _auditWriter.WriteAsync(Arg.Do<AdminAuditEntry>(received.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(new LoginCommand(email, password), CancellationToken.None);

        received.Should().ContainSingle();
        AssertNoPii(received.Single(), password, email);
    }

    private static void AssertNoPii(AdminAuditEntry entry, string password, string email)
    {
        entry.Operation.Should().NotContain(password).And.NotContain(email);
        entry.EntityName.Should().NotContain(password).And.NotContain(email);
        entry.Result.Should().NotContain(password).And.NotContain(email);
        entry.Module.Should().NotContain(password).And.NotContain(email);
        (entry.ErrorCode ?? string.Empty).Should().NotContain(password).And.NotContain(email);
        (entry.TargetEntityType ?? string.Empty).Should().NotContain(password).And.NotContain(email);
        (entry.TenantType ?? string.Empty).Should().NotContain(password).And.NotContain(email);
    }
}
