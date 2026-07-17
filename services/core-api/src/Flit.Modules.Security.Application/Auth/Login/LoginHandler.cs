using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.Login;

public sealed class LoginHandler(
    IAuthUserRepository authUserRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenIssuer jwtTokenIssuer,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext)
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(command.Password))
        {
            await AuditLoginFailedAsync(null, null, "invalid_credentials", cancellationToken).ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        var snapshot = await authUserRepository.FindByEmailAsync(email, cancellationToken);
        if (snapshot is null)
        {
            await AuditLoginFailedAsync(null, null, "invalid_credentials", cancellationToken).ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        if (!string.Equals(snapshot.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "invalid_credentials", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        if (!passwordHasher.Verify(command.Password, snapshot.PasswordHash))
        {
            await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "invalid_credentials", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        // Bloqueo temporal vigente (HU #10170, AC2): solo se revela tras verificar la
        // contraseña, para no filtrar el estado de la cuenta a terceros.
        if (snapshot.IsTemporarilySuspended)
        {
            await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "account_suspended", cancellationToken)
                .ConfigureAwait(false);
            throw new AccountSuspendedException();
        }

        // HU #10507 AC2: el usuario tuvo roles asignados alguna vez, pero todos están inactivos
        // hoy. Si nunca tuvo ningún rol asignado (TotalAssignedRolesCount == 0), el login procede
        // con normalidad (AC3) — no se bloquea.
        if (snapshot.TotalAssignedRolesCount > 0 && snapshot.ActiveRoles.Count == 0)
        {
            await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "all_roles_inactive", cancellationToken)
                .ConfigureAwait(false);
            throw new AllRolesInactiveException();
        }

        var issued = jwtTokenIssuer.IssueToken(
            snapshot.UserId,
            snapshot.Email,
            snapshot.TenantId,
            snapshot.TenantName,
            snapshot.TenantTaxId,
            snapshot.EntityType,
            snapshot.ActiveRoles,
            snapshot.PermissionSlugs);

        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                snapshot.TenantId,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "session",
                AuditVocabulary.Operations.Login,
                AuditVocabulary.Results.Success,
                ErrorCode: null,
                snapshot.UserId,
                TargetEntityType: "USER",
                snapshot.UserId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);

        return new LoginResult(issued.Token, issued.ExpiresInSeconds, "Bearer");
    }

    /// <summary>
    /// Login fallido (HU #10678): sin contraseñas ni PII en el rastro. Cuando el email no
    /// resuelve a un usuario, <paramref name="tenantId"/>/<paramref name="actorUserId"/> son
    /// <c>null</c> — el rastro igual queda con IP, fecha/hora, operación y resultado.
    /// </summary>
    private async Task AuditLoginFailedAsync(
        Guid? tenantId, Guid? actorUserId, string errorCode, CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            new AdminAuditEntry(
                tenantId,
                TenantType: null,
                AuditVocabulary.Modules.Authentication,
                EntityName: "session",
                AuditVocabulary.Operations.LoginFailed,
                AuditVocabulary.Results.Failure,
                errorCode,
                actorUserId,
                TargetEntityType: actorUserId is null ? null : "USER",
                actorUserId,
                auditContext.ClientIp,
                UserAgent: null),
            cancellationToken).ConfigureAwait(false);
}
