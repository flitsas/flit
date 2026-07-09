using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.Login;

public sealed class LoginHandler(
    IAuthUserRepository authUserRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenIssuer jwtTokenIssuer)
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(command.Password))
            throw new InvalidCredentialsException();

        var snapshot = await authUserRepository.FindByEmailAsync(email, cancellationToken);
        if (snapshot is null)
            throw new InvalidCredentialsException();

        if (!string.Equals(snapshot.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidCredentialsException();

        if (!passwordHasher.Verify(command.Password, snapshot.PasswordHash))
            throw new InvalidCredentialsException();

        // Bloqueo temporal vigente (HU #10170, AC2): solo se revela tras verificar la
        // contraseña, para no filtrar el estado de la cuenta a terceros.
        if (snapshot.IsTemporarilySuspended)
            throw new AccountSuspendedException();

        // HU #10507 AC2: el usuario tuvo roles asignados alguna vez, pero todos están inactivos
        // hoy. Si nunca tuvo ningún rol asignado (TotalAssignedRolesCount == 0), el login procede
        // con normalidad (AC3) — no se bloquea.
        if (snapshot.TotalAssignedRolesCount > 0 && snapshot.ActiveRoles.Count == 0)
            throw new AllRolesInactiveException();

        var issued = jwtTokenIssuer.IssueToken(
            snapshot.UserId,
            snapshot.Email,
            snapshot.TenantId,
            snapshot.TenantName,
            snapshot.TenantTaxId,
            snapshot.EntityType,
            snapshot.ActiveRoles,
            snapshot.PermissionSlugs);

        return new LoginResult(issued.Token, issued.ExpiresInSeconds, "Bearer");
    }
}
