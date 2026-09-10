using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneVehiclePrefill"/> (Feature #12201, HU #12206):
/// consulta el vehículo por placa para prellenar el formulario, <b>sin trámite y sin gates</b>.
///
/// <para><b>Qué reusa:</b> el <see cref="IConsultationProviderChainResolver"/> existente —el mismo
/// que corre el pre-vuelo del wizard—, así que la cadena Kyverum RUNT → Verifik, el presupuesto de
/// failover, el override por tenant y el mapeo de la respuesta
/// (<c>KyverumRuntVehicleResultMapper</c>, invocado dentro del provider) son exactamente los del
/// trámite. Aquí no hay un solo parser propio: si el mapper mejora, el prellenado mejora con él.</para>
///
/// <para><b>Qué NO hace, y es el punto de la HU:</b> no reutiliza <c>RunPreflightPreviewHandler</c>.
/// Ese handler exige tipo/familia de trámite, evalúa el gate de organismo y <b>corta con 409 cuando
/// la placa tiene un trámite activo</b>. Emitir un documento no es abrir un trámite: un vehículo con
/// expediente en curso debe prellenar igual. La garantía es estructural —este adaptador no recibe el
/// repositorio de instancias, ni el resolutor de organismos, ni la compuerta de operabilidad del OT,
/// ni la política de validación del trámite—, no una promesa del comentario.</para>
///
/// <para>Tampoco persiste: ni <c>field_values</c>, ni eventos de instancia, ni certificaciones.
/// Nunca lanza: un fallo de transporte vuelve como <c>Error</c> normalizado.</para>
/// </summary>
internal sealed class StandaloneVehiclePrefillAdapter : IStandaloneVehiclePrefill
{
    /// <summary>Mismo <c>TemplateCode</c> que usa el tramo de vehículo del pre-vuelo.</summary>
    private const string TemplateCode = "vehiculo";

    private static readonly IReadOnlyDictionary<string, string?> EmptyFields =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private readonly IConsultationProviderChainResolver _chainResolver;
    private readonly IConsultationTenantOverrideProvider _overrideProvider;

    public StandaloneVehiclePrefillAdapter(
        IConsultationProviderChainResolver chainResolver,
        IConsultationTenantOverrideProvider overrideProvider)
    {
        _chainResolver = chainResolver ?? throw new ArgumentNullException(nameof(chainResolver));
        _overrideProvider = overrideProvider ?? throw new ArgumentNullException(nameof(overrideProvider));
    }

    public async Task<StandaloneVehicleLookupResult> LookupAsync(
        Guid tenantId,
        string plate,
        string? ownerDocumentType,
        string? ownerDocumentNumber,
        CancellationToken cancellationToken = default)
    {
        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["plate"] = plate,
        };

        // El documento del propietario lo lee el provider vehicle-by-plate cuando está disponible. Es
        // OPCIONAL aquí: en el wizard lo impone el paso de consulta, pero para prellenar un documento
        // el usuario puede tener solo la placa.
        if (!string.IsNullOrWhiteSpace(ownerDocumentType))
        {
            fieldValues["owner_document_type"] = ownerDocumentType;
        }

        if (!string.IsNullOrWhiteSpace(ownerDocumentNumber))
        {
            fieldValues["owner_document_number"] = ownerDocumentNumber;
        }

        // Guid.Empty = convenio "sin instancia", el mismo que ya usan el pre-vuelo del paso 1 y el
        // lookup RUES sin trámite.
        var ctx = new ConsultationContext(Guid.Empty, tenantId, TemplateCode, fieldValues);

        ConsultationResult result;
        try
        {
            var tenantOverride = await _overrideProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
            result = await _chainResolver
                .ConsultAsync(ConsultationKind.VehiclePlate, ctx, tenantOverride, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Sin excepción cruda hacia arriba: el handler decide cómo degradar.
            return new StandaloneVehicleLookupResult(false, EmptyFields, null, "provider_unavailable");
        }

        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in result.HydratedFields)
        {
            fields[field.FieldKey] = field.ValueText;
        }

        // Distinguir "el proveedor no respondió" de "la placa no tiene antecedente": ambos llegan sin
        // campos hidratados, pero solo el primero es un 502. Mismo criterio que
        // RuesActorJuridicalLookup.IsProviderFailure para el RUES.
        if (fields.Count == 0 && RuesActorJuridicalLookup.IsProviderFailure(result))
        {
            return new StandaloneVehicleLookupResult(false, EmptyFields, result.Provider, "provider_unavailable");
        }

        return new StandaloneVehicleLookupResult(fields.Count > 0, fields, result.Provider, null);
    }
}
