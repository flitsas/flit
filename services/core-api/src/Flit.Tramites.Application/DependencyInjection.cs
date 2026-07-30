using Flit.Tramites.Application.UseCases.Catalogs;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Application.UseCases.ProcedureTypes;
using Flit.Tramites.Domain.Services;
using Flit.Tramites.Domain.Tramites.Estados;
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
        // FEATURE-08 / HU-BE-01 (CFD-01) — perfil de conformación (gate_profile) + snapshot por instancia.
        services.AddScoped<GetConformationProfileHandler>();
        services.AddScoped<UpdateConformationProfileHandler>();
        // FEATURE-08 / HU-BE-08 (CFD-12) — listado de tipos publicados para el selector de operador.
        services.AddScoped<GetPublishedProcedureTypesHandler>();

        services.AddScoped<CaptureTypeSnapshotHandler>();
        services.AddScoped<CreateProcedureInstanceHandler>();
        services.AddScoped<GetProcedureInstanceHandler>();
        services.AddScoped<ListProcedureInstancesHandler>();
        services.AddScoped<PatchFieldValuesHandler>();
        // HU #10975 (Feature #10972) — persiste en field_values lo que el OCR semántico ya extrae.
        services.AddScoped<PersistOcrFieldsHandler>();
        // HU #10990 (Feature #10972) — resuelve el RUES por actor al generar el expediente.
        services.AddScoped<IRuesActorDataResolver, RuesActorDataResolver>();
        services.AddScoped<SubmitProcedureInstanceHandler>();
        // ICT (paridad v1) — pausar/reanudar trámites ICT desde la UI de FLIT (individual + masivo).
        services.AddScoped<PauseProcedureInstanceHandler>();
        services.AddScoped<CompletePlateFlowHandler>();
        // HU #10349 — finalizar borrador (fase 2): datos completos sin exigir identidad/FUR.
        services.AddScoped<FinalizeDraftProcedureInstanceHandler>();
        // HU #10536 — marcar trámite como prioritario (ordenamiento con primacía en los listados).
        services.AddScoped<SetPriorityProcedureInstanceHandler>();
        // HU #10879 — persistir el avance del borrador por pasos (autosave del paso actual del wizard).
        services.AddScoped<SetCurrentStepProcedureInstanceHandler>();

        // N 03 (ADR-0022) — ciclo de vida de estados: servicio único de transición + endpoint
        // /transition. Puertos: el recorder de historial (HU-2) se registra abajo; el publisher
        // de outbox hacia webhooks OT (HU-3) se registra en Infraestructura
        // (ProcedureStateChangeOutboxPublisher, InfrastructureExtensions).
        services.AddScoped<ITramiteLifecycleService, TramiteLifecycleService>();
        services.AddScoped<TransitionProcedureInstanceHandler>();
        services.AddScoped<StartSubsanacionHandler>();
        services.AddScoped<CancelSubsanacionHandler>();
        services.AddScoped<GetActorsHandler>();
        services.AddScoped<PutActorsHandler>();
        // HU #10955 (AC2/AC3/AC4/AC5) — lookup de datos de contacto ya conocidos (ciudad/email/
        // dirección/teléfono) de una persona, sin gate de consentimiento.
        services.AddScoped<ActorContactLookupHandler>();
        // HU #10520 — validación de carga por tipo (MIME/tamaño) con respaldo global. El catálogo
        // (IDocumentTypeCatalog) se registra en Infraestructura; aquí solo el validador que lo consume.
        services.AddScoped<AttachmentValidator>();
        services.AddScoped<UploadAttachmentHandler>();
        services.AddScoped<PresignAttachmentHandler>();
        services.AddScoped<RegisterAttachmentHandler>();
        // Materialización de adjuntos ICT por referencia (escritura de sistema; bypassa el whitelist del front).
        services.AddScoped<RegisterIntegrationAttachmentHandler>();
        services.AddScoped<ListAttachmentsHandler>();
        services.AddScoped<DeleteAttachmentHandler>();
        services.AddScoped<DownloadAttachmentHandler>();
        services.AddScoped<GenerarImprontaAttachmentHandler>();
        // HU #11017 - el consolidado genera en cascada la impronta que falte a traves de este puerto.
        services.AddScoped<IImprontaAutoGenerator, ImprontaAutoGenerator>();
        // Diferir la impronta al paso FUR: marca el ítem de checklist sin adjuntar para no bloquear el paso 2.
        services.AddScoped<SetImprontaDiferidaHandler>();
        // RF36 — autogeneración del Certificado RUES (NIT). El cliente externo (IRuesExternalClient) se
        // registra en Infraestructura SOLO si Rues:Enabled=true; sin él, el handler cae a carga manual.
        services.AddScoped<GenerarRuesAttachmentHandler>();
        // HU #10522 (RF17/RF22) — completitud documental "gestor manda" compartida por display y gates.
        services.AddScoped<UseCases.ProcedureInstances.ChecklistMatrixCompleteness>();
        services.AddScoped<GetChecklistHandler>();
        // IT-3 (Feature #10585) — prenda: comando base (registrar/leer decisión vigente).
        services.AddScoped<RegistrarPrendaHandler>();
        services.AddScoped<GetPrendaVigenteHandler>();
        services.AddScoped<GetCommercialHandler>();
        services.AddScoped<PutCommercialHandler>();
        services.AddScoped<UseCases.Avaluos.GetSuggestedCommercialValueHandler>();
        services.AddScoped<RunPreflightHandler>();
        services.AddScoped<GetPreflightHandler>();

        // CF-02 (HU #10879/#10883) — consulta del paso 1 sin trámite creado y creación al avanzar al
        // paso 2. El store es SINGLETON: custodia en memoria las consultas del paso 1 (minutos) para
        // que crear el trámite no repita la llamada al proveedor externo.
        services.AddSingleton<IPreflightPreviewStore, InMemoryPreflightPreviewStore>();
        services.AddScoped<RunPreflightPreviewHandler>();
        services.AddScoped<CreateProcedureInstanceFromConsultaHandler>();

        // FEATURE 05 — consulta RNMC desacoplada del pre-vuelo (corre en el paso final, por actor).
        services.AddScoped<RunRnmcConsultHandler>();
        services.AddScoped<GetRnmcHandler>();
        services.AddScoped<GetWizardStateHandler>();
        // HU #10478 — proveedor de consulta resuelto por tenant, para adaptar la UI del wizard.
        services.AddScoped<UseCases.Consultations.GetConsultationConfigHandler>();

        // HU-2 (N03): puerto de historial del lifecycle — 1 fila de status_history + 1 evento por transición.
        services.AddScoped<Domain.Tramites.Estados.ITramiteTransitionRecorder, UseCases.ProcedureInstances.Estados.TramiteTransitionRecorder>();
        services.AddScoped<UseCases.ProcedureInstances.Estados.GetStatusHistoryHandler>();

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

        // HU #10866 (CF-01, Feature #10864) — Prevalidación standalone (sin trámite): upsert de
        // la entidad Person + inicio de validación biométrica con ProcedureInstanceId=null.
        services.AddScoped<UseCases.Persons.IniciarPrevalidacionHandler>();
        // HU #10943 (CF-03, Feature #10864) — editar datos de contacto (reenvío automático si cambia
        // el correo) y reenviar manualmente una prevalidación standalone.
        services.AddScoped<UseCases.Persons.EditarPrevalidacionHandler>();
        services.AddScoped<UseCases.Persons.ReenviarPrevalidacionHandler>();
        // CF-06 (Feature #11004, ADR-0036) — detalle de UNA validación por id (poll), tenant-scoped,
        // sirve tanto a standalone como a trámite.
        services.AddScoped<UseCases.Persons.GetPrevalidacionDetailHandler>();

        // Kyverum Verify (HU #10233): iniciar validación remota + procesar webhook firmado. El cliente
        // HTTP, el protector de secretos y el publisher de eventos se registran en Infraestructura.
        services.AddScoped<IniciarKyverumVerifyHandler>();
        services.AddScoped<KyverumWebhookHandler>();
        // Punto único de aplicación del resultado (webhook + reconciliación) y reconciliación por consulta
        // (fallback cuando el webhook se pierde): desatasca validaciones colgadas consultando a Kyverum.
        services.AddScoped<Identity.IdentityValidationResultApplier>();
        services.AddScoped<ReconciliarIdentidadHandler>();

        // HU #10349 (fase 2) — consumidor de IdentityValidationCompleted: encadena firma/FUR de los
        // borradores finalizados del sujeto validado. Lo invoca el procesador de outbox (Infraestructura).
        services.AddScoped<Identity.IdentityValidationCompletedConsumer>();
        // HU #10349 (fase 2) — observabilidad: consulta + reencolar eventos de identidad ATASCADOS (dead-letter).
        services.AddScoped<ListStuckIdentityValidationsHandler>();
        services.AddScoped<RequeueStuckIdentityValidationHandler>();
        services.AddScoped<RequeueAllStuckIdentityValidationsHandler>();
        // HU #10873 — alertas y recordatorios de validación de identidad (AC1/AC2), entrega POR PULL.
        services.AddScoped<ListIdentityValidationAlertsHandler>();

        // Firma electrónica + FUR. El proveedor de firma es MOCK swappable (contract-first).
        // IFurDocumentGenerator se registra en Infrastructure (FurOverlayDocumentGenerator — overlay PdfSharpCore, HU #10256).
        // MockFurDocumentGenerator se conserva para tests; solo se quitó el registro de DI.
        services.AddSingleton<Signatures.ISignatureProvider, Signatures.MockSignatureProvider>();
        // Certificado de identidad: el FUR embebe el PDF REAL de Kyverum (IKyverumCertificateClient,
        // registrado en Infraestructura, HttpClient con cookie). IIdentityCertificateGenerator (#10458,
        // QuestPDF) permanece registrado en Infraestructura; MockIdentityCertificateGenerator solo para tests.
        services.AddScoped<SolicitarFirmaHandler>();
        services.AddScoped<ListFirmasHandler>();
        services.AddScoped<SimularFirmaHandler>();
        services.AddScoped<GenerarFurHandler>();
        // ADR-0036 §D9 (HU #10916) — resolución del mandatario al aprobar (consumida por AdminOtEndpoints).
        services.AddScoped<MandatoApprovalHandler>();
        // HU #10860 (ADR-0032) — el consolidado del wizard regenera en cascada el FUR/documentos en
        // caliente vía este puerto, resuelto al mismo GenerarFurHandler (mismo scope/unidad de trabajo).
        services.AddScoped<IExpedienteHotDocumentsRegenerator>(sp => sp.GetRequiredService<GenerarFurHandler>());
        services.AddScoped<GetFurTemplateFormatHandler>(); // HU #10924 — formato de FUR por clasificación
        services.AddScoped<GenerarConsolidadoHandler>();
        // HU #11051 — gate de generación documental del GESTOR (estado final ⇒ documentación definitiva).
        // Lo consumen SOLO los endpoints de /api/v1/tramites; la regeneración interna del sistema
        // (aprobación OT, placa, identidad validada, transiciones) NO pasa por él a propósito.
        services.AddScoped<GeneracionDocumentalGestorGuard>();
        // Feature #10701 — presigned view URL inline (HU #10702) y consolidado maestro (HU #10706).
        services.AddScoped<GetAttachmentPreviewUrlHandler>();
        services.AddScoped<GenerarConsolidadoMaestroHandler>();
        // Perfil OT: visualizar el consolidado y adjuntar la Licencia de Tránsito (LT) sobre
        // trámites de clientes (se ejecutan en el scope RLS del tenant cliente vía AdminOtEndpoints).
        services.AddScoped<DescargarConsolidadoHandler>();
        services.AddScoped<AdjuntarLicenciaTransitoHandler>();
        // Descarga on-demand del certificado (PDF) de la validación de identidad desde Kyverum.
        services.AddScoped<DescargarCertificadoIdentidadHandler>();
        // Bitácora de solo lectura del ciclo de una validación (diagnóstico desde la API).
        services.AddScoped<GetIdentityAuditHandler>();
        // CF-07 (Feature #11004, ADR-0036) — misma bitácora, sin depender de instanceId (standalone + trámite).
        services.AddScoped<GetIdentityAuditByValidationHandler>();

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

        // HU #10878 (Feature #10862, CF-04, ADR-0030/ADR-0031) — cache-aside cross-trámite de
        // consultas externas, consumido por los 3 handlers de consulta de abajo.
        services.AddScoped<UseCases.Consultations.ExternalQueryCacheService>();
        services.AddScoped<UseCases.Consultations.RunConsultationHandler>();
        services.AddScoped<UseCases.Consultations.RuntPersonLookupHandler>();
        services.AddScoped<UseCases.Consultations.ValidateSoatViaRuntHandler>();
        // Lookup jurídico RUES (bifurcación del "Consultar RUNT" para persona jurídica / NIT).
        services.AddScoped<UseCases.Consultations.RuesPersonLookupHandler>();

        // OCR semántico de documentos de trámites (prompt + LLM de visión). El handler es Application;
        // el IDocumentOcrAnalyzer (mock | Anthropic según Ocr:Provider) se registra en Infraestructura
        // (AddOcr) — mismo split app-layer/infra que los consultation providers.
        services.AddScoped<Ocr.AnalyzeDocumentHandler>();

        services.AddScoped<ListProcedureEntitiesHandler>();
        services.AddScoped<ListExternalDataSourcesHandler>();
        services.AddScoped<ListConsultationTemplatesHandler>();
        services.AddScoped<ApplyConsultationTemplateFieldsHandler>();

        return services;
    }
}
