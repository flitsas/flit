namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU9 (Caso C 2026-11-09) — grupo de placa de una clase de vehículo, en su valor de contrato
/// (<c>CARRO</c> | <c>MOTO</c> | <c>MOTOCARRO</c> | <c>REMOLQUE</c> | <c>MAQUINARIA</c> | <c>null</c>). Lo implementa
/// Infrastructure con la regla única de HU8 (<c>PlateRangeRules.ResolvePlateClassGroup</c> + catálogo
/// <c>tramites.vehicle_classification_fur</c>): Tramites.Application no referencia Admin.Domain, así que
/// se expone por este puerto en vez de duplicar las listas.
/// </summary>
public interface IVehiclePlateGroupResolver
{
    /// <summary>Clase cruda (sin normalizar) ⇒ grupo de contrato; vacía, «SIN CLASE» o desconocida ⇒ <c>null</c>.</summary>
    Task<string?> ResolveContractValueAsync(string? rawVehicleClass, CancellationToken cancellationToken = default);
}
