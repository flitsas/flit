using Flit.Admin.Domain.PlatePreassign;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Infrastructure.OtClientProcedures;

/// <summary>
/// HU9 (Caso C 2026-11-09) — adaptador de <see cref="IVehiclePlateGroupResolver"/> sobre la regla de HU8
/// (<see cref="PlateClassGroupResolution"/>), para el preview del paso 1 del wizard.
/// </summary>
internal sealed class VehiclePlateGroupResolver(IFurTemplateResolver furTemplateResolver) : IVehiclePlateGroupResolver
{
    private readonly IFurTemplateResolver _furTemplateResolver = furTemplateResolver;

    public async Task<string?> ResolveContractValueAsync(string? rawVehicleClass, CancellationToken cancellationToken = default) =>
        PlateRangeRules.ToContractValue(await PlateClassGroupResolution
            .ResolveAsync(rawVehicleClass, _furTemplateResolver, cancellationToken)
            .ConfigureAwait(false));
}
