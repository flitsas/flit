using Flit.Modules.Security.Domain.PermissionOverrides;

namespace Flit.Modules.Security.Application.PermissionOverrides.GrantModule;

/// <summary>Otorga (o niega) un módulo completo a un usuario en un tenant. Validaciones de tope/ámbito: HUs posteriores.</summary>
/// <param name="AllowedPermissionIds">HU #13644: si viene, solo se otorgan las acciones de este conjunto (tope del administrador); el resto cuenta en <c>OutOfCap</c>.</param>
public sealed record GrantModuleOverridesCommand(
    Guid TenantId, Guid UserId, Guid ActorUserId, string ModuleCode, string Effect, IReadOnlySet<Guid>? AllowedPermissionIds = null);

/// <summary><c>Skipped</c> = acciones del módulo que el usuario ya tenía con un override activo (se conservan intactas).</summary>
public sealed record GrantModuleOverridesResult(string ProductCode, Guid BatchId, int Created, int Skipped, int OutOfCap = 0);

/// <summary>
/// "Módulo completo" es una FOTO (ADR-0071): crea un override por cada acción activa del módulo en este momento.
/// Una acción agregada después NO queda otorgada.
/// </summary>
public sealed class GrantModuleOverridesHandler(IUserPermissionOverrideRepository repository)
{
    public async Task<GrantModuleOverridesResult> HandleAsync(GrantModuleOverridesCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!PermissionEffects.IsValid(command.Effect))
        {
            throw new ArgumentException("El efecto debe ser 'allow' o 'deny'.", nameof(command));
        }

        var module = await repository.GetModuleActionsAsync(command.ModuleCode, ct).ConfigureAwait(false)
            ?? throw new ModuleNotFoundForOverrideException(command.ModuleCode);
        var existing = (await repository.ListActiveAsync(command.TenantId, command.UserId, module.ProductCode, ct).ConfigureAwait(false))
            .Select(o => o.PermissionId).ToHashSet();

        var batchId = Guid.NewGuid();
        var notYetOverridden = module.Actions.Where(a => !existing.Contains(a.PermissionId)).ToList();
        var pending = notYetOverridden
            .Where(a => command.AllowedPermissionIds is null || command.AllowedPermissionIds.Contains(a.PermissionId))
            .ToList();
        var overrides = pending
            .Select(a => new UserPermissionOverride(
                Guid.NewGuid(), command.TenantId, command.UserId, module.ProductCode, a.PermissionId,
                command.Effect, PermissionOverrideSources.ModuleSnapshot, batchId, command.ActorUserId))
            .ToList();
        var audit = pending
            .Select(a => new UserPermissionAuditEntry(
                command.TenantId, command.ActorUserId, command.UserId, module.ProductCode, module.ModuleCode,
                a.Slug, "MODULE_SNAPSHOT", "OK", null, null, command.Effect, batchId))
            .ToList();

        if (overrides.Count > 0)
        {
            await repository.AddAsync(command.TenantId, overrides, audit, ct).ConfigureAwait(false);
        }

        return new GrantModuleOverridesResult(
            module.ProductCode, batchId, overrides.Count, module.Actions.Count - notYetOverridden.Count, notYetOverridden.Count - pending.Count);
    }
}
