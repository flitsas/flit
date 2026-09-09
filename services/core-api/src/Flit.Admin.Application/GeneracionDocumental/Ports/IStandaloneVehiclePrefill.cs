namespace Flit.Admin.Application.GeneracionDocumental.Ports;

/// <summary>
/// Resultado crudo de la consulta de vehículo por placa, ya normalizado por el adaptador.
/// </summary>
/// <param name="Found">
/// <c>true</c> si el RUNT devolvió atributos del vehículo. Una placa sin antecedente llega con
/// <c>Found = false</c> y <c>Error = null</c>: no es una caída del proveedor.
/// </param>
/// <param name="Fields">
/// Campos hidratados con el vocabulario canónico del repo (<c>plate</c>, <c>vehicle_brand</c>, …),
/// tal como los produce el mapper de trámites. La traducción a las variables del anexo normativo la
/// hace el handler, no el adaptador.
/// </param>
/// <param name="Provider">Proveedor que respondió (<c>kyverum_runt</c>, <c>verifik</c>, …).</param>
/// <param name="Error"><c>provider_unavailable</c> o <c>null</c>. Nunca la excepción cruda.</param>
public sealed record StandaloneVehicleLookupResult(
    bool Found,
    IReadOnlyDictionary<string, string?> Fields,
    string? Provider,
    string? Error);

/// <summary>
/// Puerto acotado de la consulta de vehículo por placa para el prellenado standalone (CF-25).
///
/// <para><b>Sin gates de trámite.</b> El adaptador ejecuta la cadena de proveedores existente y
/// nada más: no evalúa duplicidad de trámite activo, no evalúa el organismo de tránsito, no crea ni
/// lee ninguna instancia y no persiste <c>field_values</c>. Un vehículo con un trámite abierto
/// —que en el wizard produce un 409— debe poder prellenar un documento igual, porque el documento
/// no es un trámite.</para>
///
/// <para>El adaptador tampoco lanza: cualquier fallo de transporte vuelve como <c>Error</c>
/// normalizado.</para>
/// </summary>
public interface IStandaloneVehiclePrefill
{
    Task<StandaloneVehicleLookupResult> LookupAsync(
        Guid tenantId,
        string plate,
        string? ownerDocumentType,
        string? ownerDocumentNumber,
        CancellationToken cancellationToken = default);
}
