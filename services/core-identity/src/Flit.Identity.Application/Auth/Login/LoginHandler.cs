using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.Login;

/// <summary>
/// Login actual: verifica las credenciales con <see cref="CredentialVerifier"/> (todas las reglas de HU #12422,
/// #10170, #10507 y #10678 viven allí) y emite el JWT de siempre. El claim <c>dom</c> del JWT liga la sesión al
/// dominio de emisión (AC5, <c>Flit.Api.Authorization.DomainBindingMiddleware</c>).
/// </summary>
public sealed class LoginHandler(
    IAuthUserRepository authUserRepository,
    IPasswordHasher passwordHasher,
    IJwtTokenIssuer jwtTokenIssuer,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ITenantNetworkMembership networkMembership,
    IDomainContextAccessor domainContext)
{
    private readonly CredentialVerifier _verifier = new(
        authUserRepository, passwordHasher, auditWriter, auditContext, networkMembership, domainContext);

    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var verified = await _verifier.VerifyAsync(command.Email, command.Password, cancellationToken)
            .ConfigureAwait(false);
        var snapshot = verified.User;

        var issued = jwtTokenIssuer.IssueToken(
            snapshot.UserId,
            snapshot.Email,
            snapshot.TenantId,
            snapshot.TenantName,
            snapshot.TenantTaxId,
            snapshot.EntityType,
            snapshot.TenantType,
            snapshot.IsGroupParent,
            snapshot.ActiveRoles,
            snapshot.PermissionSlugs,
            verified.DomainClaim);

        return new LoginResult(issued.Token, issued.ExpiresInSeconds, "Bearer", verified.NetworkHost);
    }
}
