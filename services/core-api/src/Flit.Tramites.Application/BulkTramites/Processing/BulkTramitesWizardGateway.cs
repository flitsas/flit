using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Integration;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Adaptador del puerto sobre los casos de uso reales del wizard (HU #12523). NO decide nada: la
/// regla de qué se crea y qué no vive en <see cref="BulkTramitesBatchProcessor"/>. Aquí solo se
/// arman los requests y se desempaquetan las tuplas.
/// </summary>
public sealed class BulkTramitesWizardGateway(
    RunPreflightPreviewHandler previewHandler,
    CreateProcedureInstanceFromConsultaHandler createHandler,
    PutActorsHandler actorsHandler,
    RuntPersonLookupHandler personLookupHandler,
    RuesPersonLookupHandler companyLookupHandler,
    IBulkTramitesLegalRepresentativeDirectory representativeDirectory,
    ITransitOfficeResolver transitOfficeResolver) : IBulkTramitesWizardGateway
{
    /// <summary>El proveedor respondió pero no conoce el documento: dato mal escrito o persona sin registro RUNT.</summary>
    public const string ConductorNoEncontrado = "conductor_no_encontrado";

    /// <summary>La consulta al proveedor se cayó (red, 5xx, timeout). Distinto de «no existe».</summary>
    public const string ConsultaConductorFallida = "consulta_conductor_fallida";

    /// <summary>RUES respondió pero no conoce el NIT (HU #12538).</summary>
    public const string EmpresaNoEncontrada = "empresa_no_encontrada";

    /// <summary>La consulta a RUES se cayó (HU #12538). Distinto de «no existe», igual que en conductor.</summary>
    public const string ConsultaEmpresaFallida = "consulta_empresa_fallida";

    /// <summary>
    /// Lo que <see cref="RuesPersonLookupHandler"/> devuelve como error cuando el proveedor no
    /// responde (ver <c>RuesActorJuridicalLookup.ConsultAsync</c>): es el único error suyo que
    /// merece reintento.
    /// </summary>
    private const string RuesProviderUnavailable = "provider_unavailable";

    /// <summary>
    /// Motivo de fila cuando el organismo escrito en el Excel no es uno de los habilitados de la
    /// empresa. Se distingue del error del wizard a propósito: el usuario tiene que saber que el
    /// dato malo es ESE, no el vehículo.
    /// </summary>
    public const string OrganismoNoHabilitado = "organismo_transito_no_habilitado";

    public async Task<(string? PreviewToken, string? Error)> PreviewVehicleAsync(
        BulkTramitesRowContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (transitOfficeId, resolveError) = await ResolveTransitOfficeAsync(context, ct)
            .ConfigureAwait(false);
        if (resolveError is not null)
        {
            return (null, resolveError);
        }

        var (result, error, _, _) = await previewHandler.HandleAsync(
            new PreflightPreviewRequest(
                context.TenantId,
                context.FamilyCode,
                context.Vin,
                context.Plate,
                context.OwnerDocumentType,
                context.OwnerDocumentNumber,
                transitOfficeId,
                context.ProcedureTypeCode),
            ct).ConfigureAwait(false);

        if (error is not null || result is null)
        {
            return (null, error);
        }

        // El handler responde OK con el semáforo en rojo: la decisión de crear o no vive en los checks.
        var gate = BulkTramitesVehicleGate.Evaluate(result.Checks);
        return gate is null ? (result.PreviewToken, null) : (null, gate);
    }

    /// <summary>
    /// Traduce el NOMBRE del organismo escrito en el Excel al id que exige el wizard. Solo aplica
    /// donde la plantilla lo pide (matrícula); en el resto no hay nombre que resolver y el
    /// organismo lo fija el RUNT.
    /// </summary>
    private async Task<(Guid? Id, string? Error)> ResolveTransitOfficeAsync(
        BulkTramitesRowContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.TransitOfficeName))
        {
            return (null, null);
        }

        var resuelto = await transitOfficeResolver
            .ResolveEnabledByNameAsync(context.TenantId, context.TransitOfficeName.Trim(), ct)
            .ConfigureAwait(false);

        return resuelto is null ? (null, OrganismoNoHabilitado) : (resuelto.Id, null);
    }

    public async Task<(Guid? ProcedureInstanceId, string? Error)> CreateTramiteAsync(
        BulkTramitesRowContext context, string? previewToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (transitOfficeId, resolveError) = await ResolveTransitOfficeAsync(context, ct)
            .ConfigureAwait(false);
        if (resolveError is not null)
        {
            return (null, resolveError);
        }

        var (result, error, existingId, _) = await createHandler.HandleAsync(
            new CreateFromConsultaRequest(
                context.TenantId,
                context.CreatedByUserId,
                context.FamilyCode,
                context.Vin,
                context.Plate,
                context.OwnerDocumentType,
                context.OwnerDocumentNumber,
                previewToken,
                transitOfficeId,
                ProcedureTypeCode: context.ProcedureTypeCode),
            ct).ConfigureAwait(false);

        // El handler puede fallar DESPUÉS de haber creado la instancia (p. ej. el preflight
        // autoritativo se cae): en ese caso devuelve el id junto al error, y el trámite existe. Se
        // propaga tal cual para que el procesador lo registre como creado-con-pendiente en vez de
        // dejar un trámite huérfano marcado como «no creado».
        return (result?.Instance.Id ?? existingId, error);
    }

    public async Task<(string? FullName, string? Error)> LookupPersonAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken ct)
    {
        RuntPersonDto? persona;
        string? error;
        try
        {
            (persona, error) = await personLookupHandler
                .HandleAsync(procedureInstanceId, tenantId, documentType, documentNumber, ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // La caída del proveedor es un resultado de la fila, no un fallo del lote.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return (null, ConsultaConductorFallida);
        }

        if (error is not null)
        {
            return (null, error);
        }

        if (persona is { ProviderUnavailable: true })
        {
            return (null, ConsultaConductorFallida);
        }

        // El handler responde Found=false con el mismo shape que un hallazgo: aquí se vuelve un
        // código, que es lo que el resumen del lote sabe traducir.
        return persona is { Found: true } && !string.IsNullOrWhiteSpace(persona.FullName)
            ? (persona.FullName.Trim(), null)
            : (null, ConductorNoEncontrado);
    }

    public async Task<(BulkTramitesCompanyLookup? Company, string? Error)> LookupCompanyAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        string nit,
        CancellationToken ct)
    {
        RuesPersonDto? empresa;
        string? error;
        try
        {
            (empresa, error) = await companyLookupHandler
                .HandleAsync(procedureInstanceId, tenantId, nit, ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // La caída del proveedor es un resultado de la fila, no un fallo del lote.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return (null, ConsultaEmpresaFallida);
        }

        if (error is not null)
        {
            return (null, error == RuesProviderUnavailable ? ConsultaEmpresaFallida : error);
        }

        if (empresa is not { Found: true } || string.IsNullOrWhiteSpace(empresa.RazonSocial))
        {
            return (null, EmpresaNoEncontrada);
        }

        // El directorio se consulta DESPUÉS de saber que la empresa existe: sin RUES no hay razón
        // social y el wizard tampoco precarga nada en ese caso.
        var directorio = await representativeDirectory
            .FindByNitAsync(tenantId, nit, ct)
            .ConfigureAwait(false);

        return (new BulkTramitesCompanyLookup(empresa.RazonSocial.Trim(), directorio), null);
    }

    public async Task<string?> SaveActorsAsync(
        Guid procedureInstanceId,
        Guid tenantId,
        IReadOnlyList<ActorInput> actors,
        CancellationToken ct)
    {
        var (_, error) = await actorsHandler
            .HandleAsync(procedureInstanceId, tenantId, new PutActorsRequest(actors), ct)
            .ConfigureAwait(false);

        return error;
    }
}
