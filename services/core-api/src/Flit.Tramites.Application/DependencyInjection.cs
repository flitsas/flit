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
        services.AddScoped<ListProcedureInstancesHandler>();
        services.AddScoped<PatchFieldValuesHandler>();
        services.AddScoped<SubmitProcedureInstanceHandler>();
        // HU #10349 — finalizar borrador (fase 2): datos completos sin exigir identidad/FUR.
        services.AddScoped<FinalizeDraftProcedureInstanceHandler>();
        services.AddScoped<GetActorsHandler>();
        services.AddScoped<PutActorsHandler>();
        services.AddScoped<UploadAttachmentHandler>();
        services.AddScoped<PresignAttachmentHandler>();
        services.AddScoped<RegisterAttachmentHandler>();
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
        // HU #10234 — vista transversal del submódulo "Validaciones de Identidad" (todas las instancias).
        services.AddScoped<ListTenantBiometricValidationsHandler>();
        services.AddScoped<GetBiometriaByTokenHandler>();
        services.AddScoped<CompletarBiometriaHandler>();
        services.AddScoped<SimularBiometriaHandler>();
        // HU #10350 — asegurar identidad vigente (reuso de validación ≤30 días) al guardar la parte.
        services.AddScoped<EnsureIdentityHandler>();

        // Kyverum Verify (HU #10233): iniciar validación remota + procesar webhook firmado. El cliente
        // HTTP, el protector de secretos y el publisher de eventos se registran en Infraestructura.
        services.AddScoped<IniciarKyverumVerifyHandler>();
        services.AddScoped<KyverumWebhookHandler>();

        // HU #10349 (fase 2) — consumidor de IdentityValidationCompleted: encadena firma/FUR de los
        // borradores finalizados del sujeto validado. Lo invoca el procesador de outbox (Infraestructura).
        services.AddScoped<Identity.IdentityValidationCompletedConsumer>();
        // HU #10349 (fase 2) — observabilidad: consulta + reencolar eventos de identidad ATASCADOS (dead-letter).
        services.AddScoped<ListStuckIdentityValidationsHandler>();
        services.AddScoped<RequeueStuckIdentityValidationHandler>();
        services.AddScoped<RequeueAllStuckIdentityValidationsHandler>();

        // Firma electrónica + FUR. El proveedor de firma es MOCK swappable (contract-first).
        // IFurDocumentGenerator se registra en Infrastructure (FurOverlayDocumentGenerator — overlay PdfSharpCore, HU #10256).
        // MockFurDocumentGenerator se conserva para tests; solo se quitó el registro de DI.
        services.AddSingleton<Signatures.ISignatureProvider, Signatures.MockSignatureProvider>();
        services.AddSingleton<Documents.IIdentityCertificateGenerator, Documents.MockIdentityCertificateGenerator>();
        services.AddScoped<SolicitarFirmaHandler>();
        services.AddScoped<ListFirmasHandler>();
        services.AddScoped<SimularFirmaHandler>();
        services.AddScoped<GenerarFurHandler>();
        services.AddScoped<GenerarConsolidadoHandler>();

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
