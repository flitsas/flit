using Flit.Admin.Domain.Companies.VehicleOwnership;
using Flit.Admin.Domain.Companies.Whitelist;

namespace Flit.Admin.Application.Companies.VehicleOwnership;

/// <summary>
/// Implementación del interceptor de propiedad vehicular (HU #10191, RF04/RF05).
///
/// Orden de evaluación:
/// <list type="number">
///   <item>Si la política efectiva no exige <c>only_own_vehicles</c> → continúa (sin validar).</item>
///   <item>Si el correo del usuario está en la lista blanca → continúa (excepción, AC2).</item>
///   <item>Si el vehículo NO pertenece al tenant → se bloquea con el mensaje del AC1.</item>
///   <item>Si pertenece → continúa.</item>
/// </list>
///
/// La política efectiva la resuelve el llamador vía <c>ITenantPolicyResolver</c>
/// (snapshot para trámites in-flight, AC3): este guard solo lee
/// <see cref="VehicleOwnershipCheckContext.EffectivePolicy"/>, nunca la política live.
/// </summary>
public sealed class VehicleOwnershipGuard : IVehicleOwnershipGuard
{
    private readonly IVehicleTenantOwnershipChecker _ownershipChecker;
    private readonly IWhitelistRepository _whitelist;

    public VehicleOwnershipGuard(
        IVehicleTenantOwnershipChecker ownershipChecker,
        IWhitelistRepository whitelist)
    {
        _ownershipChecker = ownershipChecker ?? throw new ArgumentNullException(nameof(ownershipChecker));
        _whitelist = whitelist ?? throw new ArgumentNullException(nameof(whitelist));
    }

    public async Task<VehicleOwnershipGuardResult> ValidateTransferStartAsync(
        VehicleOwnershipCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // (1) La regla no aplica: el tenant no restringe a vehículos propios.
        if (!context.EffectivePolicy.OnlyOwnVehicles)
        {
            return VehicleOwnershipGuardResult.Allowed();
        }

        // (2) AC2: el usuario está exento por lista blanca.
        if (!string.IsNullOrWhiteSpace(context.UserEmail))
        {
            var whitelisted = await _whitelist
                .IsEmailWhitelistedAsync(context.TenantId, context.UserEmail, cancellationToken)
                .ConfigureAwait(false);

            if (whitelisted)
            {
                return VehicleOwnershipGuardResult.Allowed();
            }
        }

        // (3) AC1: bloquea si el vehículo no es del tenant.
        var owned = await _ownershipChecker
            .IsVehicleOwnedByTenantAsync(context.TenantId, context.VehicleIdentifier, cancellationToken)
            .ConfigureAwait(false);

        return owned
            ? VehicleOwnershipGuardResult.Allowed()
            : VehicleOwnershipGuardResult.Blocked(VehicleOwnershipMessages.VehicleNotOwned);
    }
}
