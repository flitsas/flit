using Flit.Tramites.Application.UseCases.Catalogs;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Tramites.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddTramitesApplication(this IServiceCollection services)
    {
        services.AddScoped<IProcedureTypeValidator, ProcedureTypeValidator>();

        services.AddScoped<CreateProcedureTypeHandler>();
        services.AddScoped<ListProcedureTypesHandler>();
        services.AddScoped<GetProcedureTypeHandler>();
        services.AddScoped<UpdateProcedureTypeHandler>();
        services.AddScoped<DeleteProcedureTypeHandler>();
        services.AddScoped<PublishProcedureTypeHandler>();
        services.AddScoped<ArchiveProcedureTypeHandler>();
        services.AddScoped<ValidateProcedureTypeHandler>();
        services.AddScoped<GetConformationRulesHandler>();
        services.AddScoped<UpsertConformationRulesHandler>();
        services.AddScoped<GetProcedureStepsHandler>();
        services.AddScoped<UpsertProcedureStepsHandler>();
        services.AddScoped<GetProcedureTypeConfigurationHandler>();

        services.AddScoped<CreateProcedureInstanceHandler>();
        services.AddScoped<GetProcedureInstanceHandler>();
        services.AddScoped<PatchFieldValuesHandler>();
        services.AddScoped<SubmitProcedureInstanceHandler>();
        services.AddScoped<GetActorsHandler>();
        services.AddScoped<PutActorsHandler>();
        services.AddScoped<UploadAttachmentHandler>();
        services.AddScoped<ListAttachmentsHandler>();
        services.AddScoped<DeleteAttachmentHandler>();
        services.AddScoped<DownloadAttachmentHandler>();
        services.AddScoped<GetChecklistHandler>();
        services.AddScoped<GetCommercialHandler>();
        services.AddScoped<PutCommercialHandler>();
        services.AddScoped<RunPreflightHandler>();
        services.AddScoped<GetPreflightHandler>();
        services.AddScoped<GetWizardStateHandler>();

        // Biométrica (Slice 6, mock). El scorer es un MOCK determinista; se reemplazará por uno real
        // (proveedor biométrico) sin tocar handlers. Contract-first, igual que los consultation providers.
        services.AddSingleton<Biometrics.IBiometricScorer, Biometrics.MockBiometricScorer>();
        services.AddScoped<IniciarBiometriaHandler>();
        services.AddScoped<ListBiometriaHandler>();
        services.AddScoped<GetBiometriaByTokenHandler>();
        services.AddScoped<CompletarBiometriaHandler>();
        services.AddScoped<SimularBiometriaHandler>();

        // Firma electrónica + FUR (Slice 7, mock). El proveedor de firma y el generador de
        // documentos son MOCKs swappables (contract-first, igual que el scorer biométrico):
        // se reemplazan por ZapSign / generador PDF real sin tocar los handlers.
        services.AddSingleton<Signatures.ISignatureProvider, Signatures.MockSignatureProvider>();
        services.AddSingleton<Documents.IFurDocumentGenerator, Documents.MockFurDocumentGenerator>();
        services.AddSingleton<Documents.IIdentityCertificateGenerator, Documents.MockIdentityCertificateGenerator>();
        services.AddScoped<SolicitarFirmaHandler>();
        services.AddScoped<ListFirmasHandler>();
        services.AddScoped<SimularFirmaHandler>();
        services.AddScoped<GenerarFurHandler>();

        // Portal público de participantes + consent Ley 1581 (Slice 7 Part B). Magic-link con token
        // hasheado (solo SHA-256 en BD); el portal agrega/encadena biométrica y firma reusando los
        // handlers existentes (no se duplican reglas).
        services.AddScoped<InvitarParticipanteHandler>();
        services.AddScoped<ListParticipantesHandler>();
        services.AddScoped<ReinvitarParticipanteHandler>();
        services.AddScoped<GetPortalByTokenHandler>();
        services.AddScoped<AceptarConsentimientoHandler>();
        services.AddScoped<SubirDocumentoPortalHandler>();
        services.AddScoped<FinalizarPortalHandler>();
        services.AddScoped<GetFirmaUrlPortalHandler>();
        services.AddScoped<SimularFirmaPortalHandler>();

        services.AddScoped<UseCases.Consultations.RunConsultationHandler>();
        services.AddScoped<UseCases.Consultations.RuntPersonLookupHandler>();

        services.AddScoped<ListProcedureEntitiesHandler>();
        services.AddScoped<ListExternalDataSourcesHandler>();
        services.AddScoped<ListConsultationTemplatesHandler>();
        services.AddScoped<ApplyConsultationTemplateFieldsHandler>();

        return services;
    }
}
