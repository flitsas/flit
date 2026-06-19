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

        if (snapshot.IsTemporarilySuspended)
            throw new InvalidCredentialsException();

        if (!passwordHasher.Verify(command.Password, snapshot.PasswordHash))
            throw new InvalidCredentialsException();

        var issued = jwtTokenIssuer.IssueToken(
            snapshot.UserId,
            snapshot.Email,
            snapshot.TenantId,
            snapshot.RoleId,
            snapshot.RoleCode,
            snapshot.PermissionSlugs);

        return new LoginResult(issued.Token, issued.ExpiresInSeconds, "Bearer");
    }
}
