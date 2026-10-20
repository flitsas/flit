using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Modules.Security.Application.Auth.Login;

/// <summary>Resultado de una verificación de credenciales exitosa.</summary>
/// <param name="DomainClaim">Valor del claim <c>dom</c>: <c>flit</c> o el host de la red (HU #12422).</param>
/// <param name="NetworkHost">Host de la red cuando el login fue por su dominio; <c>null</c> en FLIT.</param>
public sealed record VerifiedCredentials(UserAuthSnapshot User, bool IsSuperAdmin, string DomainClaim, string? NetworkHost);

/// <summary>
/// HU #12990 (FLIT Suite A-05) — verificación de credenciales extraída de <see cref="LoginHandler"/> (decisión de la
/// espiga A-04) para que la compartan el login actual, que emite el JWT de siempre, y el login del hub, que abre la
/// sesión OIDC. Conserva todas las reglas del login:
/// <para>
/// HU #12422 (Feature #12369, ADR-0060 D3) — acceso acotado por dominio. El orden es SIEMPRE:
/// buscar usuario → verificar el hash (real o señuelo, tiempo constante) → pertenencia a la red del
/// dominio de la petición → bloqueo temporal / roles inactivos. Un usuario existente sin asignación
/// en la red del dominio recibe EXACTAMENTE el mismo 401 que un usuario inexistente (anti-enumeración,
/// AC2). El login exitoso y cada fallo quedan en la auditoría (HU #10678).
/// </para>
/// </summary>
public sealed class CredentialVerifier(
    IAuthUserRepository authUserRepository,
    IPasswordHasher passwordHasher,
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext,
    ITenantNetworkMembership networkMembership,
    IDomainContextAccessor domainContext)
{
    private const string SuperAdminRoleCode = "SuperAdmin";
    private const string FlitDomain = "flit";

    public async Task<VerifiedCredentials> VerifyAsync(string rawEmail, string password, CancellationToken cancellationToken)
    {
        var email = (rawEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            await AuditLoginFailedAsync(null, null, "invalid_credentials", cancellationToken).ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        var snapshot = await authUserRepository.FindByEmailAsync(email, cancellationToken);

        // AC2/AC8 (HU #12422) — el hash se verifica SIEMPRE, también cuando el usuario no existe
        // (contra el hash señuelo de IPasswordHasher.DummyHash), para que el tiempo de respuesta no
        // delate ni la existencia del email ni su pertenencia a una red.
        var passwordMatches = passwordHasher.Verify(
            password,
            snapshot?.PasswordHash ?? passwordHasher.DummyHash);

        if (snapshot is null || !passwordMatches)
        {
            await AuditLoginFailedAsync(snapshot?.TenantId, snapshot?.UserId, "invalid_credentials", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

        var isSuperAdmin = snapshot.ActiveRoles.Any(role =>
            string.Equals(role.Code, SuperAdminRoleCode, StringComparison.OrdinalIgnoreCase));

        var membership = await networkMembership.ResolveAsync(snapshot.TenantId, cancellationToken)
            .ConfigureAwait(false);

        string domainClaim;
        string? networkHost = null;

        if (domainContext.Kind == DomainKind.Network)
        {
            // AC1/AC2/AC8 — dominio de la red R: solo entran la cabeza y las hijas de R; un
            // SuperAdmin NUNCA se autentica por el dominio de una red (AC6). Todo lo demás (usuario
            // sin asignación en R, o de otra red B) es el MISMO 401 que una credencial inválida.
            var belongsToThisNetwork = !isSuperAdmin
                && membership.HeadTenantId is { } head
                && head == domainContext.HeadTenantId;

            if (!belongsToThisNetwork)
            {
                await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "invalid_credentials", cancellationToken)
                    .ConfigureAwait(false);
                throw new InvalidCredentialsException();
            }

            networkHost = domainContext.Host;
            domainClaim = domainContext.Host ?? FlitDomain;
        }
        else
        {
            // AC3 — dominio de FLIT: un usuario de una red MARCA_BLANCA con dominio ACTIVO debe
            // entrar por su propio dominio (salvo SuperAdmin, que siempre entra por FLIT). Sin
            // dominio activo, Concesión o sin red: sigue igual que hoy (AC6).
            if (!isSuperAdmin && membership is { IsMarcaBlancaNetwork: true, ActiveHost: { } activeHost })
            {
                throw new NetworkDomainRequiredException(activeHost);
            }

            domainClaim = FlitDomain;
        }

        // Bloqueo temporal vigente (HU #10170 AC2) y roles inactivos (HU #10507 AC2) se evalúan
        // DESPUÉS de verificar el hash Y la pertenencia a la red (HU #12422, ADR-0060 D3).
        if (!string.Equals(snapshot.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            await AuditLoginFailedAsync(snapshot.TenantId, snapshot.UserId, "invalid_credentials", cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidCredentialsException();
        }

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

        return new VerifiedCredentials(snapshot, isSuperAdmin, domainClaim, networkHost);
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
