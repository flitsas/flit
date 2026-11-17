using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

/// <param name="TenantId">Empresa objetivo (x-flit-tenant-id).</param>
public sealed record ChangeProductRoleCommand(Guid TenantId, ProductActor Actor, string ProductCode, Guid UserId, string RoleCode);

/// <summary>
/// ADR-0089 regla 4 / SP-9=a — cambia el rol del producto de un usuario de la empresa. Solo cierra y crea asignaciones de
/// ESE producto: el rol de Trámites (u otro producto) del usuario se conserva. No usa <c>AssignRoleHandler</c> (B-1), que
/// cierra todas las asignaciones del tenant. Idempotente: el mismo rol responde la fila sin cambios.
/// </summary>
public sealed class AssignProductRoleHandler(IProductRoleRepository repo)
{
    public async Task<ProductRoleOutcome> HandleAsync(ChangeProductRoleCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ProductRoleRules.EnsureAllowedRole(cmd.ProductCode, cmd.RoleCode);
        ProductRoleRules.EnsureCanGrant(cmd.Actor, cmd.ProductCode, cmd.RoleCode);
        ProductRoleRules.EnsureNotSelf(cmd.Actor, cmd.UserId);

        var member = await ProductRoleRules.LoadTargetAsync(repo, cmd.TenantId, cmd.UserId, cmd.ProductCode, ct).ConfigureAwait(false);
        ProductRoleRules.EnsureNotProtected(member);
        ProductRoleRules.EnsureCanTouch(cmd.Actor, cmd.ProductCode, member);

        var current = member.CurrentRole
            ?? throw ProductRoleRules.Fail(ProductRoleErrorCodes.UsuarioSuspendido, ProductRoleErrorKind.FailedPrecondition, "El usuario está suspendido en el producto: reactívelo primero.");
        if (string.Equals(current.Code, cmd.RoleCode, StringComparison.Ordinal))
            return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, member), current.Code, current.Code, Changed: false);

        var otherAdmins = await repo.CountOtherActiveAdminsAsync(cmd.TenantId, ProductRoleRules.AdminRole(cmd.ProductCode), cmd.UserId, ct).ConfigureAwait(false);
        ProductRoleRules.EnsureNotLastAdmin(cmd.ProductCode, current.Code, otherAdmins);

        var role = await repo.FindAssignableRoleAsync(cmd.ProductCode, cmd.RoleCode, ct).ConfigureAwait(false)
            ?? throw ProductRoleRules.Fail(ProductRoleErrorCodes.RolNoPermitido, ProductRoleErrorKind.InvalidArgument, "El rol no está disponible en el catálogo.");

        // CR-4: la pre-guarda de arriba es la respuesta rápida; la que vale es la del repositorio, dentro de la transacción.
        var guarda = string.Equals(current.Code, ProductRoleRules.AdminRole(cmd.ProductCode), StringComparison.Ordinal) ? current.Code : null;
        await repo.ReplaceProductRoleAsync(cmd.TenantId, cmd.UserId, cmd.ProductCode, role.Id, cmd.Actor.UserId, guarda, ct).ConfigureAwait(false);
        var updated = member with { CurrentRole = role };
        return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, updated), current.Code, role.Code, Changed: true);
    }
}
