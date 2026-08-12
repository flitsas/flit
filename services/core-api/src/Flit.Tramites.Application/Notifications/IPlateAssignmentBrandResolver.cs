namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// HU #11486 (ADR-0046) — resuelve la variante FLIT/Renting del correo de asignación de placa
/// por NIT del tenant cliente, no por canal.
/// </summary>
public interface IPlateAssignmentBrandResolver
{
    /// <summary>Marca a partir del <c>tax_id</c> del tenant (normalizado).</summary>
    PlateAssignmentEmailBrand ResolveFromTaxId(string? taxId);

    /// <summary>
    /// Carga el tenant cliente, resuelve marca por NIT y registra warning si difiere del canal.
    /// </summary>
    Task<PlateAssignmentEmailBrand> ResolveForClientTenantAsync(
        Guid clientTenantId,
        CancellationToken cancellationToken = default);
}
