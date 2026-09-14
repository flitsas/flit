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
    ITransitOfficeResolver transitOfficeResolver) : IBulkTramitesWizardGateway
{
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

        return (result?.PreviewToken, error);
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
