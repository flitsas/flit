using Flit.Tramites.Application.UseCases.ProcedureInstances;

namespace Flit.Tramites.Application.BulkTramites.Processing;

/// <summary>
/// Adaptador del puerto sobre los casos de uso reales del wizard (HU #12523). NO decide nada: la
/// regla de qué se crea y qué no vive en <see cref="BulkTramitesBatchProcessor"/>. Aquí solo se
/// arman los requests y se desempaquetan las tuplas.
/// </summary>
public sealed class BulkTramitesWizardGateway(
    RunPreflightPreviewHandler previewHandler,
    CreateProcedureInstanceFromConsultaHandler createHandler,
    PutActorsHandler actorsHandler) : IBulkTramitesWizardGateway
{
    public async Task<(string? PreviewToken, string? Error)> PreviewVehicleAsync(
        BulkTramitesRowContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (result, error, _, _) = await previewHandler.HandleAsync(
            new PreflightPreviewRequest(
                context.TenantId,
                context.FamilyCode,
                context.Vin,
                context.Plate,
                context.OwnerDocumentType,
                context.OwnerDocumentNumber,
                TransitOfficeId: null,
                context.ProcedureTypeCode),
            ct).ConfigureAwait(false);

        return (result?.PreviewToken, error);
    }

    public async Task<(Guid? ProcedureInstanceId, string? Error)> CreateTramiteAsync(
        BulkTramitesRowContext context, string? previewToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

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
                TransitOfficeId: null,
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
