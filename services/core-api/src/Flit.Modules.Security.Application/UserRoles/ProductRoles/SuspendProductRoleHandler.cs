using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

public sealed record ProductUserCommand(Guid TenantId, ProductActor Actor, string ProductCode, Guid UserId);

/// <summary>
/// ADR-0089 regla 4 / SP-12=a — suspende al usuario SOLO en el producto: cierra su asignación del producto y deja la marca
/// en <c>security.product_role_suspensions</c> con el rol que tenía. La cuenta, sus sesiones y sus roles de otros productos
/// no cambian (no usa <c>SuspendUserHandler</c>, B-4). Guarda propia del último admin del producto. Idempotente.
/// </summary>
public sealed class SuspendProductRoleHandler(IProductRoleRepository repo)
{
    public async Task<ProductRoleOutcome> HandleAsync(ProductUserCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ProductRoleRules.EnsureNotSelf(cmd.Actor, cmd.UserId);

        var member = await ProductRoleRules.LoadTargetAsync(repo, cmd.TenantId, cmd.UserId, cmd.ProductCode, ct).ConfigureAwait(false);
        ProductRoleRules.EnsureNotProtected(member);
        ProductRoleRules.EnsureCanTouch(cmd.Actor, cmd.ProductCode, member);

        if (member.CurrentRole is not { } current)
        {
            var role = member.SuspendedRole?.Code;
            return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, member), role, role, Changed: false);
        }

        var otherAdmins = await repo.CountOtherActiveAdminsAsync(cmd.TenantId, ProductRoleRules.AdminRole(cmd.ProductCode), cmd.UserId, ct).ConfigureAwait(false);
        ProductRoleRules.EnsureNotLastAdmin(cmd.ProductCode, current.Code, otherAdmins);

        // CR-4: la guarda que vale es la del repositorio, dentro de la transacción (SELECT … FOR UPDATE).
        var guarda = string.Equals(current.Code, ProductRoleRules.AdminRole(cmd.ProductCode), StringComparison.Ordinal) ? current.Code : null;
        await repo.SuspendAsync(cmd.TenantId, cmd.UserId, cmd.ProductCode, current.Id, cmd.Actor.UserId, guarda, ct).ConfigureAwait(false);
        var updated = member with { CurrentRole = null, SuspendedRole = current };
        return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, updated), current.Code, null, Changed: true);
    }
}
