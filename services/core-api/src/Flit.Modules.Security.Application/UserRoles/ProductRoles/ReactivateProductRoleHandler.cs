using Flit.Modules.Security.Domain.UserRoles.ProductRoles;

namespace Flit.Modules.Security.Application.UserRoles.ProductRoles;

/// <summary>
/// ADR-0089 regla 4 / SP-12=a — reactiva al usuario en el producto: recrea la asignación con el rol guardado en la marca y
/// la borra. Si entretanto el hub le dio otro rol del producto, borra la marca y responde ROL_YA_VIGENTE (el cliente
/// refresca la fila). Sin marca y con rol vigente: idempotente.
/// </summary>
public sealed class ReactivateProductRoleHandler(IProductRoleRepository repo)
{
    public async Task<ProductRoleOutcome> HandleAsync(ProductUserCommand cmd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ProductRoleRules.EnsureNotSelf(cmd.Actor, cmd.UserId);

        var member = await ProductRoleRules.LoadTargetAsync(repo, cmd.TenantId, cmd.UserId, cmd.ProductCode, ct).ConfigureAwait(false);
        ProductRoleRules.EnsureNotProtected(member);
        ProductRoleRules.EnsureCanTouch(cmd.Actor, cmd.ProductCode, member);

        if (member.SuspendedRole is not { } suspended)
        {
            var role = member.CurrentRole?.Code;
            return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, member), role, role, Changed: false);
        }

        if (member.CurrentRole is not null)
        {
            await repo.ClearSuspensionAsync(cmd.TenantId, cmd.UserId, cmd.ProductCode, ct).ConfigureAwait(false);
            throw ProductRoleRules.Fail(ProductRoleErrorCodes.RolYaVigente, ProductRoleErrorKind.FailedPrecondition, "El usuario ya tiene un rol vigente del producto.");
        }

        await repo.ReactivateAsync(cmd.TenantId, cmd.UserId, cmd.ProductCode, suspended.Id, cmd.Actor.UserId, ct).ConfigureAwait(false);
        var updated = member with { CurrentRole = suspended, SuspendedRole = null };
        return new ProductRoleOutcome(ProductRoleRules.RowFor(cmd.Actor, cmd.ProductCode, updated), null, suspended.Code, Changed: true);
    }
}
