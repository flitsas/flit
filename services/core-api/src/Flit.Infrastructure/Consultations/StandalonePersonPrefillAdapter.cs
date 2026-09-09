using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneRuntPersonPrefill"/> (Feature #12201, HU #12206):
/// consulta la identidad de una persona natural por documento <b>sin instancia de trámite</b>.
///
/// <para><b>Por qué no se reusa <c>RuntPersonLookupHandler</c>:</b> ese handler cuelga de la
/// instancia —la busca en el repositorio, corta con <c>instance_not_found</c> si no existe, toma su
/// <c>ReferenceNumber</c> como contexto y escribe un evento <c>RuntPersonaConsultada</c> en el
/// expediente—. Nada de eso tiene sentido para prellenar un documento que no es un trámite.</para>
///
/// <para>Lo que sí se reusa es el núcleo: la MISMA cadena <c>kyverum_runt_conductor →
/// verifik_conductor</c> por <see cref="IConsultationProviderChainResolver"/>, con el override del
/// tenant, y el MISMO servicio de caché — invocado con <c>sourceProcedureInstanceId = null</c>, que
/// ese servicio ya admite por firma. Fail-open: si la caché no puede escribir, el prellenado
/// responde igual.</para>
/// </summary>
internal sealed class StandalonePersonPrefillAdapter : IStandaloneRuntPersonPrefill
{
    private const string TemplateCode = "conductor";
    private const string RuntSourceCode = "RUNT";

    private readonly IConsultationProviderChainResolver _chainResolver;
    private readonly IConsultationTenantOverrideProvider _overrideProvider;
    private readonly ExternalQueryCacheService _cacheService;
    private readonly TimeProvider _timeProvider;

    public StandalonePersonPrefillAdapter(
        IConsultationProviderChainResolver chainResolver,
        IConsultationTenantOverrideProvider overrideProvider,
        ExternalQueryCacheService cacheService,
        TimeProvider? timeProvider = null)
    {
        _chainResolver = chainResolver ?? throw new ArgumentNullException(nameof(chainResolver));
        _overrideProvider = overrideProvider ?? throw new ArgumentNullException(nameof(overrideProvider));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StandaloneRuntPersonResult> LookupAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        var mapped = MapDocumentType(documentType);
        if (mapped is null)
        {
            return new StandaloneRuntPersonResult(false, null, null, null, null, "unsupported_document_type");
        }

        var fieldValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["document_type"] = mapped,
            ["document_number"] = documentNumber,
        };

        // Guid.Empty = sin instancia. El provider de conductor no usa el id para nada más que trazas.
        var ctx = new ConsultationContext(Guid.Empty, tenantId, TemplateCode, fieldValues);

        ConsultationResult result;
        try
        {
            var tenantOverride = await _overrideProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
            result = await _chainResolver
                .ConsultAsync(ConsultationKind.Conductor, ctx, tenantOverride, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new StandaloneRuntPersonResult(false, null, null, null, null, "provider_unavailable");
        }

        var fullName = RuesActorJuridicalLookup.GetHydrated(result.HydratedFields, "person_full_name");
        var found = !string.IsNullOrWhiteSpace(fullName);

        if (!found && RuesActorJuridicalLookup.IsProviderFailure(result))
        {
            return new StandaloneRuntPersonResult(false, null, null, null, result.Provider, "provider_unavailable");
        }

        // Caché con instancia NULA: el servicio recibe Guid? y no exige trámite. Fail-open — un fallo
        // aquí no puede cambiar la respuesta que el usuario ya tiene calculada.
        try
        {
            await _cacheService
                .SavePersonResultAsync(
                    tenantId,
                    RuntSourceCode,
                    documentType,
                    documentNumber,
                    sourceProcedureInstanceId: null,
                    result.HydratedFields,
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Silencio deliberado: la caché es una optimización, no parte del contrato del prellenado.
        }

        return new StandaloneRuntPersonResult(
            found,
            found ? fullName : null,
            found ? RuesActorJuridicalLookup.GetHydrated(result.HydratedFields, "person_first_name") : null,
            found ? RuesActorJuridicalLookup.GetHydrated(result.HydratedFields, "person_last_name") : null,
            result.Provider,
            null);
    }

    /// <summary>
    /// Mismo mapeo documentType FLIT → proveedor que aplica el lookup del wizard (CC→CC, CE→CE,
    /// PAS→PA, TI→PPT). NIT devuelve null: una persona jurídica va por el endpoint de RUES, no por la
    /// cadena de conductor.
    /// </summary>
    private static string? MapDocumentType(string documentType) =>
        documentType.ToUpperInvariant() switch
        {
            "CC" => "CC",
            "CE" => "CE",
            "PAS" => "PA",
            "TI" => "PPT",
            "NIT" => null,
            _ => documentType,
        };
}

/// <summary>
/// Adaptador del puerto <see cref="IStandaloneActorContactLookup"/>: respaldo de contacto del
/// prellenado de persona natural. Delega en <see cref="ActorContactLookupHandler"/>, el mismo caso de
/// uso que sirve <c>GET /api/v1/tramites/actors/contact-lookup</c> — que ya funciona <b>sin
/// instancia</b> y es tenant-scoped por repositorio.
///
/// <para>Su contrato es deliberadamente pobre: ciudad, correo, dirección y teléfono; nunca nombre ni
/// documento. Aquí solo se toman ciudad y dirección, que es lo que el documento de transferencia
/// necesita (domicilio de la parte); correo y teléfono no son variables del anexo.</para>
/// </summary>
internal sealed class StandaloneActorContactLookupAdapter : IStandaloneActorContactLookup
{
    private readonly ActorContactLookupHandler _handler;

    public StandaloneActorContactLookupAdapter(ActorContactLookupHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public async Task<StandaloneContactLookupResult> LookupAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (result, error) = await _handler
                .HandleAsync(tenantId, documentType, documentNumber, cancellationToken)
                .ConfigureAwait(false);

            if (error is not null || result is null)
            {
                return new StandaloneContactLookupResult(false, null, null, error ?? "provider_unavailable");
            }

            var found = !string.IsNullOrWhiteSpace(result.Ciudad) || !string.IsNullOrWhiteSpace(result.Direccion);
            return new StandaloneContactLookupResult(found, result.Ciudad, result.Direccion, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new StandaloneContactLookupResult(false, null, null, "provider_unavailable");
        }
    }
}
