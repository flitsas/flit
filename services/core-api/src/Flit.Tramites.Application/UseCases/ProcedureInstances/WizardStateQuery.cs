using System.Text.Json;
using System.Text.Json.Nodes;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Un paso del wizard, en la forma congelada del contrato.</summary>
public sealed record WizardStepDto(
    int Index,
    string Key,
    string Label,
    string Status,           // complete | incomplete | locked
    IReadOnlyList<string> Reasons)
{
    /// <summary>
    /// FEATURE-08 / HU-BE-06 (CFD-09): tipo de renderer del paso (section_type) cuando el wizard es
    /// dinámico. Null en el camino estático (matrícula/traspaso hardcoded).
    /// </summary>
    public string? SectionType { get; init; }

    /// <summary>Configuración del renderer para el frontend (SectionRendererRegistry). Null si no aplica.</summary>
    public JsonNode? SectionConfig { get; init; }

    /// <summary>
    /// Todas las secciones del paso, en orden (CFD-09). <see cref="SectionType"/> es la primera y se
    /// mantiene por compatibilidad del contrato; un paso con varias secciones las expone aquí para
    /// que el frontend las renderice completas.
    /// </summary>
    public IReadOnlyList<string>? SectionTypes { get; init; }
}

/// <summary>
/// Estado server-driven del wizard, derivado de los gates del dominio.
/// <para><c>Status</c> = estado de negocio actual (<see cref="TramiteEstado"/>) y
/// <c>AllowedTransitions</c> = destinos permitidos por <see cref="TramiteStateMachine"/> (N 03):
/// la UI solo muestra acciones de transición que la máquina permite — el backend manda. Los
/// gates de cada transición se validan al ejecutarla (POST /transition), no aquí.</para>
/// </summary>
public sealed record WizardStateDto(
    string Modalidad,
    string? TipologiaCodigo,
    int TotalSteps,          // 5 matrícula | 6 traspaso
    IReadOnlyList<WizardStepDto> Steps,
    bool CanSubmit,
    IReadOnlyList<string> Blockers,
    string Status,
    IReadOnlyList<string> AllowedTransitions)
{
    /// <summary>
    /// HU #10548 — si el OT destino tiene la validación de identidad deshabilitada, es <c>false</c>
    /// y el frontend oculta el paso de identidad (AC3 / HU #10549). Default <c>true</c> (se exige).
    /// </summary>
    public bool IdentityValidationEnabled { get; init; } = true;

    /// <summary>
    /// FEATURE 05 — <c>true</c> si el RNMC aplica a este trámite (el OT destino lo exige y la compañía
    /// no lo inhabilitó para ese OT): el frontend muestra la fecha de expedición del documento en los
    /// actores, la consulta y genera el certificado. Default <c>false</c>: si el RNMC no aplica, la
    /// fecha de expedición se oculta (no se pide un dato que no se va a usar).
    /// </summary>
    public bool RnmcEnabled { get; init; }

    /// <summary>
    /// HU #10879 — paso actual PERSISTIDO (autosave del avance del wizard). Si NO es null, PRIMA como
    /// punto de retoma al reabrir el borrador (AC2): el frontend abre en esta <c>Key</c> de paso. Si es
    /// null, el frontend cae al paso DERIVADO de los gates (comportamiento previo, sin regresión).
    /// </summary>
    public string? PersistedCurrentStep { get; init; }

    /// <summary>Subsanación activa sobre rechazado (edición sin cambiar status de negocio).</summary>
    public bool SubsanacionActiva { get; init; }

    /// <summary>Veces que se activó la subsanación en este expediente.</summary>
    public int SubsanacionCount { get; init; }

    /// <summary>
    /// Migración V1→V2 — el trámite viene de V1; no se capturó paso a paso en V2.
    /// <para>
    /// Lo consume el frontend para PEDIRLE al gestor las consultas que el trámite no trae: los
    /// resultados de RUNT y SIMIT de V1 no se migran —son perecederos, la propia configuración de V1
    /// caduca el SIMIT a los cinco minutos, y no quedan atados al trámite—, así que un borrador
    /// migrado llega al pre-vuelo en blanco y hay que correrlas antes de radicar.
    /// </para>
    /// <para>
    /// Ese porqué se queda AQUÍ: la UI solo destaca la acción. Contarle al operador de dónde viene el
    /// trámite no le cambia lo que tiene que hacer y siembra la duda de si llegó incompleto.
    /// </para>
    /// </summary>
    public bool EsMigrado { get; init; }

    /// <summary>
    /// Compañía+OT: <c>true</c> (default) exige certificado de prenda; <c>false</c> si el opt-out
    /// <c>document_optional</c> estaba vigente al crear el trámite (snapshot). El wizard usa este
    /// flag para pintar Obligatorio/Opcional en la carga del certificado.
    /// </summary>
    public bool PrendaDocumentRequired { get; init; } = true;

    /// <summary>
    /// ADR-0050 — identidad del tipo con el que se conformó el expediente, para que el asistente
    /// titule el trámite que se está haciendo. Sin esto, el frontend solo tenía la familia y
    /// rotulaba «Matrícula Inicial» cualquier cosa que no fuera un traspaso.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// ADR-0050 — capacidades del tipo, tomadas del mismo <c>gate_profile</c> que gobierna los gates
    /// del backend (snapshot congelado, con respaldo en el catálogo vivo).
    /// <para>Es lo que le faltaba al asistente para dejar de decidir por modalidad: qué partes pide,
    /// si lleva datos comerciales, si tiene puerta de prenda y por qué identificador entra el
    /// vehículo. Sin ellas, un tipo de la familia OTROS se podía elegir y dibujar, pero por dentro
    /// se comportaba como una matrícula.</para>
    /// </summary>
    public WizardCapabilitiesDto? Capabilities { get; init; }
}

/// <summary>
/// Capacidades del tipo que el asistente necesita para armarse (ADR-0050). Es una proyección
/// deliberadamente PARCIAL de <c>ProcedureTypeGateProfile</c>: solo lo que cambia lo que el gestor
/// ve o captura. Lo que solo afecta a validaciones del servidor —<c>validateOtOperability</c>,
/// <c>simitMode</c>— no se publica: el frontend no debe poder reimplementar un gate del backend.
/// </summary>
/// <param name="EntryMode">
/// Identificador con el que entra el vehículo: <c>VIN</c> (aún no tiene placa) o <c>PLATE</c>.
/// </param>
/// <param name="RequiresSeller">Hay parte vendedora. En la familia OTROS el titular no vende.</param>
/// <param name="RequiresBuyer">Hay parte compradora o titular.</param>
/// <param name="AllowsMultipleBuyer">La parte compradora admite varias personas.</param>
/// <param name="RequiresCommercialValue">El trámite lleva valor y fecha de venta.</param>
/// <param name="RequiresBiometrics">Se valida identidad.</param>
/// <param name="BiometricActors">Actores a validar (<c>OWNER</c>, <c>BUYER</c>).</param>
/// <param name="HasPrendaGate">La decisión de prenda es una puerta y no una declaración.</param>
public sealed record WizardCapabilitiesDto(
    string? EntryMode,
    bool RequiresSeller,
    bool RequiresBuyer,
    bool AllowsMultipleBuyer,
    bool RequiresCommercialValue,
    bool RequiresBiometrics,
    IReadOnlyList<string> BiometricActors,
    bool HasPrendaGate)
{
    internal static WizardCapabilitiesDto From(ProcedureTypeGateProfile profile) =>
        new(
            profile.EntryMode,
            profile.RequiresSeller,
            profile.RequiresBuyer,
            profile.AllowsMultipleBuyer,
            profile.RequiresCommercialValue,
            profile.RequiresBiometrics,
            profile.BiometricActors,
            profile.HasPrendaGate);
}

/// <summary>
/// Compone el estado del wizard server-driven. Carga el grafo persistido, lo mapea a los
/// contextos tipados del dominio (<see cref="MatriculaGateContext"/>/<see cref="TraspasoGateContext"/>),
/// invoca los gates por paso (fuente única de verdad) y traduce a status + reasons.
///
/// <para><b>Mapeo persistencia → GateContext:</b>
/// actores → <see cref="ParteDatos"/> + <see cref="RuntSnapshot"/> (RUNT se asume consultado
/// cuando el actor tiene documento; el RUNT vive en field_values, sin entidad propia en este slice);
/// último preflight → <see cref="PreflightSnapshot"/> (Overall);
/// comercial → ValorVenta, exigido AHORA por el gate del paso de documentos (traspaso 4: los datos
/// comerciales se absorbieron ahí, paridad con matrícula);
/// checklist (<see cref="ChecklistEngine"/> sobre adjuntos) → completitud del paso de documentos
/// (HU #10935: paso 4 en traspaso, paso 3 en matrícula — después de los actores);
/// biométrica → <see cref="BiometriaSnapshot"/>, exigida por el gate del paso de Identidad (traspaso 5:
/// ambas partes; matrícula 4: comprador, evaluado aparte por el flag <c>IdentidadAprobada</c>). El
/// paso de FUR (diferido, IO) se marca incomplete con reason explícita ("fur_pendiente"/
/// "pendiente_firma"), NO se bloquea con error.</para>
/// </summary>
public sealed class GetWizardStateHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationPolicy? identityPolicy = null,
    ChecklistMatrixCompleteness? matrixCompleteness = null,
    ISignatureVaultPolicy? vaultPolicy = null,
    IConsultationBlockingPolicy? blockingPolicy = null,
    IRnmcRequirementPolicy? rnmcPolicy = null,
    IConsultationRestrictionPolicy? restrictionPolicy = null,
    IDynamicProceduresPolicy? dynamicPolicy = null,
    IProcedureTypeSnapshotRepository? snapshotRepo = null,
    IPrendaDocumentRequirementPolicy? prendaDocumentRequirementPolicy = null,
    IProcedureTypeRepository? typeRepo = null,
    IProcedureInstancePrendaRepository? prendaRepo = null,
    IResolvedChecklistMatrixProvider? checklistMatrixProvider = null)
{
    public const string PendienteBiometria = "pendiente_biometria";
    public const string PendienteFirma = "pendiente_firma";

    /// <summary>El tipo de trámite no tiene pasos configurados: no hay wizard que construir.</summary>
    public const string TipoSinParametrizar = "tipo_sin_parametrizar";
    public const string FurPendiente = "fur_pendiente";

    // FEATURE-08 / HU-BE-06 — flag F08_DynamicProcedures (default deshabilitado → camino estático).
    private readonly IDynamicProceduresPolicy _dynamicPolicy =
        dynamicPolicy ?? NullDynamicProceduresPolicy.Instance;

    // HU #10548 — política de exigibilidad de identidad por OT (default permisivo en tests).
    private readonly IIdentityValidationPolicy _identityPolicy =
        identityPolicy ?? NullIdentityValidationPolicy.Instance;

    // ADR-0025 §4 / HU #10645 — baúl de firmas: un actor NIT cubierto cuenta como identidad aprobada.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

    // FEATURE 05 — política de bloqueo por criterio y OT (default permisivo en tests): decide si los
    // comparendos bloquean el gate del paso 4 de traspaso.
    private readonly IConsultationBlockingPolicy _blockingPolicy =
        blockingPolicy ?? NullConsultationBlockingPolicy.Instance;

    // FEATURE 05 — exigibilidad del RNMC por OT (default: no exige) y consultas que la compañía
    // inhabilitó para el OT (default: nada restringido). Juntas deciden si el RNMC aplica y, por tanto,
    // si el frontend muestra la fecha de expedición del documento.
    private readonly IRnmcRequirementPolicy _rnmcPolicy =
        rnmcPolicy ?? NullRnmcRequirementPolicy.Instance;
    private readonly IConsultationRestrictionPolicy _restrictionPolicy =
        restrictionPolicy ?? NullConsultationRestrictionPolicy.Instance;

    // CF-06 (HU #10881) — override OT del documento de prenda (independiente del semáforo de
    // gravámenes). Default permisivo (nunca exige) cuando no hay política cableada (tests).
    private readonly IPrendaDocumentRequirementPolicy _prendaDocumentRequirementPolicy =
        prendaDocumentRequirementPolicy ?? NullPrendaDocumentRequirementPolicy.Instance;

    // 2026-08-12 — la decisión de prenda vive en un agregado aparte (no cuelga de ProcedureInstance),
    // y el override del OT la necesita para no exigir un documento que la UI no ofrece cargar. Sin
    // repo (tests, listado de trámites) la decisión queda null ⇒ el override calla, igual que antes.
    private readonly IProcedureInstancePrendaRepository? _prendaRepo = prendaRepo;

    // CFD-06 — matriz documental resuelta por tipo (+ overrides del OT). El camino dinámico la
    // necesita para que DocumentRequirementGate evalúe requisitos reales; sin ella caía siempre al
    // booleano agregado DocumentosCompletos y CFD-06 no llegaba nunca al motor. Sin proveedor
    // (tests) se conserva ese fallback.
    private readonly IResolvedChecklistMatrixProvider? _checklistMatrixProvider = checklistMatrixProvider;

    public async Task<(WizardStateDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Migración V1→V2 — un trámite migrado en estado terminal es una foto de solo lectura: se
        // resuelve SIEMPRE por el camino estático (ComputeState → BuildReadonlySnapshot), aunque el
        // tenant tenga el wizard dinámico habilitado. Evita que el camino dinámico intente gatear una
        // foto histórica.
        var esMigradoTerminal = instance.IsMigrated && TramiteEstado.EsFinal(instance.Status);

        // FEATURE-08 / HU-BE-06 (CFD-09): wizard dinámico flag-guarded. Solo cuando F08_DynamicProcedures
        // está habilitado para el tenant Y la instancia tiene snapshot (tipo dinámico). En cualquier otro
        // caso se preserva el camino estático (BuildMatricula/BuildTraspaso) sin regresión (AC-02).
        // Identidad PER-PERSONA (documento del actor), no por instancia: se referencia la validación
        // vigente de la persona en N trámites sin clonar (HU #10350).
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct, _vaultPolicy);

        // HU #10548 — si el OT destino deshabilita la identidad, se trata como satisfecha (el paso no
        // bloquea el submit) y se expone el flag para que el wizard oculte el paso (AC3 / HU #10549).
        var identityRequired = await _identityPolicy.IsIdentityValidationRequiredAsync(
            instance.TenantId, TransitOfficeIdFromFieldValues(instance), ct);
        var partesEfectivas = identityRequired
            ? identidadAprobada
            : IdentitySatisfiedForAllParties(identidadAprobada);

        // HU #10522 (RF17/RF22) — el gestor manda la completitud documental si tiene matriz.
        var docsCompletos = matrixCompleteness is null
            ? null
            : await matrixCompleteness.TryComputeCompletoAsync(instance, tenantId, ct);

        var otId = TransitOfficeIdFromFieldValues(instance);

        // FEATURE 05 — ¿los comparendos bloquean el paso 4 (comprador con multas) para el OT destino?
        // El gate simit_multas bloqueaba SIEMPRE antes de esta feature: sin override explícito se
        // preserva ese comportamiento (?? true); solo un override de la compañía lo cambia.
        var blockingRules = await _blockingPolicy.GetAsync(instance.TenantId, otId, ct);
        var comparendosBloquean = blockingRules.Override(ConsultationBlockingCriteria.Fines) ?? true;

        // FEATURE 05 — el RNMC aplica según el opt-in de la compañía para el OT destino ("Consultar
        // RNMC"); si no hay decisión explícita, cae al requisito del OT. Misma condición que dispara la
        // consulta en el preflight. Solo entonces el frontend muestra la fecha de expedición.
        var rnmcRequired = await _rnmcPolicy.IsRnmcRequiredAsync(instance.TenantId, otId, ct);
        var rnmcRestrictions = await _restrictionPolicy.GetAsync(instance.TenantId, otId, ct);
        var rnmcEnabled = rnmcRestrictions.SettingOf(ConsultationRestrictionKinds.Rnmc) ?? rnmcRequired;

        // CF-06 (HU #10881) — override del OT (independiente del semáforo de gravámenes): si aplica
        // y falta el documento de prenda, canSubmit debe reflejarlo con el mismo código de bloqueo
        // que el gate de preparación (TramiteLifecycleService), para que el wizard y el submit
        // coincidan. Aplica a CUALQUIER modalidad con OT: la matrícula inicial que constituye prenda
        // (vehículo nuevo financiado) es justo el caso que el semáforo de gravámenes no ve.
        var prendaOtBlocker = await PrendaOtBlockerAsync(instance, ct);

        // Actores a los que el tipo de trámite exige pasar por el RUNT, y los documentos que se
        // consultaron de verdad en este trámite. Sin repositorio de tipos (tests) no hay exigencia.
        var runtExigido = await ResolveRuntExigidoAsync(instance, tenantId, ct);

        var prendaDocumentRequired = await ResolvePrendaDocumentRequiredAsync(instance, ct);

        // ADR-0050 / CFD-09 — el wizard se conforma SIEMPRE desde el tipo: un solo motor, sin flag.
        // La conformación sale del snapshot congelado al crear y, si no lo hay, del catálogo vivo
        // (ResolveConformationAsync).
        //
        // La paridad con el antiguo camino estático está cubierta por DynamicVsStaticParityGapsTests:
        // bloqueo de pasos diferidos, razón según entryMode, semáforo del pre-vuelo, bloqueos duros en
        // el paso de documentos, actor por sección y el FUR fuera de los blockers. El camino estático
        // que queda debajo solo actúa si el tipo NO tiene pasos parametrizados, y desaparece cuando
        // los 21 tipos estén sembrados.
        //
        // La bifurcación va DESPUÉS de resolver las señales, no antes: cuando estaba arriba, el camino
        // dinámico se saltaba identidad, RNMC, prenda del OT y la matriz documental, y devolvía los
        // defaults del DTO.
        if (!esMigradoTerminal)
        {
            var conformation = await ResolveConformationAsync(instance, id, tenantId, ct);
            if (conformation is not null && conformation.Steps.Count > 0)
            {
                var dynamicState = await BuildDynamicStateAsync(
                    instance, conformation, partesEfectivas, prendaOtBlocker,
                    comparendosBloquean, runtExigido, ct);

                // HU #10879 — el paso persistido prima como punto de retoma también en el dinámico.
                return (AnnotateInstanceFlags(
                    dynamicState with
                    {
                        IdentityValidationEnabled = identityRequired,
                        RnmcEnabled = rnmcEnabled,
                        PrendaDocumentRequired = prendaDocumentRequired,
                    },
                    instance), null);
            }
        }

        var state = AnnotateInstanceFlags(
            ComputeState(
                instance, partesEfectivas, docsCompletos, comparendosBloquean, prendaOtBlocker,
                runtExigido) with
            {
                IdentityValidationEnabled = identityRequired,
                RnmcEnabled = rnmcEnabled,
                PrendaDocumentRequired = prendaDocumentRequired,
            },
            instance);
        return (state, null);
    }

    /// <summary>
    /// Política compañía+OT del certificado de prenda (snapshot al <see cref="ProcedureInstance.CreatedAt"/>).
    /// </summary>
    private Task<bool> ResolvePrendaDocumentRequiredAsync(ProcedureInstance instance, CancellationToken ct) =>
        _prendaDocumentRequirementPolicy.IsRequiredAsync(
            instance.TenantId,
            instance.TransitOfficeId ?? TransitOfficeIdFromFieldValues(instance),
            instance.CreatedAt,
            ct);

    /// <summary>
    /// FEATURE-08 / HU-BE-06 — computa el estado del wizard desde el snapshot del tipo (gate_profile +
    /// stepSectionTypes) y las MISMAS señales de la instancia que el camino estático, delegando en
    /// <see cref="DynamicGateEvaluator"/>. Mapea el resultado al contrato con <c>sectionType</c> por paso.
    /// </summary>
    /// <summary>
    /// Conformación del wizard: perfil de gates + pasos con sus <c>section_type</c>.
    /// </summary>
    private sealed record WizardConformation(
        ProcedureTypeGateProfile GateProfile,
        IReadOnlyList<DynamicWizardStep> Steps);

    /// <summary>
    /// Resuelve la conformación del expediente (ADR-0050). Precedencia:
    /// <list type="number">
    /// <item>el <b>snapshot</b> congelado al crear — así un cambio del catálogo no reconfigura un
    /// expediente en curso;</item>
    /// <item>el <b>catálogo vivo</b>, para expedientes anteriores al snapshot o creados sin él.</item>
    /// </list>
    /// Devuelve <c>null</c> si el tipo no tiene pasos parametrizados: ahí no hay wizard que construir.
    /// </summary>
    private async Task<WizardConformation?> ResolveConformationAsync(
        ProcedureInstance instance, Guid id, Guid tenantId, CancellationToken ct)
    {
        if (snapshotRepo is not null)
        {
            var snapshot = await snapshotRepo.GetByInstanceIdAsync(id, tenantId, ct);
            if (snapshot is not null)
            {
                var fromSnapshot = FromSnapshot(snapshot);
                if (fromSnapshot is not null)
                    return fromSnapshot;
            }
        }

        return FromCatalog(instance);
    }

    private static WizardConformation? FromSnapshot(ProcedureTypeSnapshotRecord snapshot)
    {
        var root = JsonNode.Parse(snapshot.Snapshot) as JsonObject ?? [];
        var gateProfile = ProcedureTypeGateProfile.FromJson(root["gateProfile"]?.ToJsonString());

        var steps = new List<DynamicWizardStep>();
        if (root["stepSectionTypes"] is JsonArray stepArr)
        {
            foreach (var node in stepArr)
            {
                if (node is not JsonObject stepObj)
                    continue;

                var stepCode = (stepObj["stepCode"] as JsonValue)?.ToString() ?? string.Empty;

                // Antes se tomaba solo sectionTypes[0] y las demás secciones del paso desaparecían
                // en silencio, con sus gates sin evaluar.
                var sectionTypes = new List<string>();
                if (stepObj["sectionTypes"] is JsonArray sts)
                {
                    foreach (var st in sts)
                    {
                        var value = (st as JsonValue)?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                            sectionTypes.Add(value);
                    }
                }

                if (sectionTypes.Count == 0)
                    sectionTypes.Add(ProcedureSectionTypes.GenericForm);

                var sectionCodes = new List<string>();
                if (stepObj["sectionCodes"] is JsonArray scs)
                {
                    foreach (var sc in scs)
                    {
                        var value = (sc as JsonValue)?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                            sectionCodes.Add(value);
                    }
                }

                steps.Add(new DynamicWizardStep(stepCode, sectionTypes)
                {
                    SectionCodes = sectionCodes,
                    StepTitle = (stepObj["stepTitle"] as JsonValue)?.ToString(),
                });
            }
        }

        return steps.Count > 0 ? new WizardConformation(gateProfile, steps) : null;
    }

    private static WizardConformation? FromCatalog(ProcedureInstance instance) =>
        FromCatalogSteps(instance.ProcedureType);

    /// <summary>Conformación desde los pasos del tipo; <c>null</c> si no tiene ninguno.</summary>
    private static WizardConformation? FromCatalogSteps(ProcedureType? type)
    {
        if (type is null || type.Steps.Count == 0)
            return null;

        var steps = type.Steps
            .Where(st => st.IsActive)
            .OrderBy(st => st.SortOrder)
            .Select(st => new DynamicWizardStep(
                st.Code,
                [.. st.Sections.OrderBy(sec => sec.SortOrder).Select(sec => sec.SectionType)])
            {
                SectionCodes = [.. st.Sections.OrderBy(sec => sec.SortOrder).Select(sec => sec.Code)],
                StepTitle = st.Title,
            })
            .Where(st => st.SectionTypes.Count > 0)
            .ToList();

        return steps.Count > 0
            ? new WizardConformation(ProcedureTypeGateProfile.FromJson(type.GateProfile), steps)
            : null;
    }

    private async Task<WizardStateDto> BuildDynamicStateAsync(
        ProcedureInstance instance,
        WizardConformation conformation,
        IReadOnlySet<string> partesEfectivas,
        string? prendaOtBlocker,
        bool comparendosBloquean,
        RuntConsultaExigida? runtExigido,
        CancellationToken ct)
    {
        // CFD-06 — requisitos reales del tipo (+ overrides del OT). Con la matriz cargada el gate
        // evalúa documento a documento; sin ella cae al booleano agregado, como hacía siempre.
        var documentRequirements = await ResolveDynamicDocumentRequirementsAsync(instance, ct);

        // La decisión de prenda vive en un agregado aparte y la necesita la sección prenda_decision.
        var prendaVigente = _prendaRepo is null
            ? null
            : await _prendaRepo.GetVigenteAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);

        return BuildDynamicStateCore(
            instance, conformation, partesEfectivas, prendaOtBlocker, comparendosBloquean,
            runtExigido, documentRequirements, prendaVigente);
    }

    /// <summary>
    /// Núcleo del wizard dinámico, SIN IO: compone el contexto desde el grafo ya cargado y lo evalúa.
    /// <para>Lo comparten el detalle del wizard —que además resuelve por IO la matriz documental y la
    /// prenda— y el listado de trámites, que necesita el mismo progreso por expediente y no puede
    /// pagar una consulta por fila.</para>
    /// </summary>
    private static WizardStateDto BuildDynamicStateCore(
        ProcedureInstance instance,
        WizardConformation conformation,
        IReadOnlySet<string> partesEfectivas,
        string? prendaOtBlocker,
        bool comparendosBloquean,
        RuntConsultaExigida? runtExigido,
        IReadOnlyList<DocumentRequirementItem> documentRequirements,
        ProcedureInstancePrenda? prendaVigente)
    {
        var gateProfile = conformation.GateProfile;
        var steps = conformation.Steps;

        var fv = FieldValues(instance);
        var comprador = ParteOf(instance, "comprador");
        var vendedor = ParteOf(instance, "vendedor");
        var runtComprador = RuntOf(instance, "comprador", runtExigido);
        var runtVendedor = RuntOf(instance, "vendedor", runtExigido);
        var preflight = PreflightOf(instance);
        var simitComprador = SimitOf(instance, comprador, preflight);

        var ctx = new DynamicWizardContext
        {
            VehiculoConsultado = HasVehiculoConsulta(fv),
            PreflightProviderError = preflight?.ProviderError == true,
            PreflightVehiculoNoEncontrado = preflight?.VehiculoNoEncontrado == true,
            DocumentosCompletos = DocumentosObligatoriosCompletos(instance),
            HasBuyer = comprador is not null,
            BuyerRuntConsultado = runtComprador?.Consultado == true,
            HasSeller = vendedor is not null,
            SellerRuntConsultado = runtVendedor?.Consultado == true,
            BuyerCompleto = ParteCompletaRule.EstaCompleta(comprador),
            SellerCompleto = ParteCompletaRule.EstaCompleta(vendedor),
            ValorVenta = instance.Commercial?.ValorVenta ?? 0m,
            // partesEfectivas y no las aprobadas en crudo: si el OT deshabilita la identidad, las
            // partes cuentan como satisfechas y el paso biométrico no bloquea (HU #10548).
            BiometricsApproved = MapPartiesToEntityCodes(partesEfectivas),
            FurGenerado = FurGenerado(instance),
            PlateRequestCompleted = PlateRequestCompleted(fv),
            UploadedDocumentCodes = new HashSet<string>(
                instance.Attachments.Select(a => a.Tipo), StringComparer.OrdinalIgnoreCase),
            DocumentRequirements = documentRequirements,
            PrendaVigente = prendaVigente,
            AttachmentTipos = instance.Attachments.Select(a => a.Tipo).ToList(),
            CompradorConComparendos = (simitComprador?.TotalComparendos ?? 0) > 0,
            ComparendosBloquean = comparendosBloquean,
            PreflightRed = preflight?.Overall == "red",
            RiesgoAceptado = RiesgoAceptado(instance),
        };

        var result = DynamicGateEvaluator.Evaluate(gateProfile, steps, ctx);

        var wizardSteps = result.Steps
            .Select(s => new WizardStepDto(
                s.Index, s.Key,
                // El título configurado manda: es el que distingue "Vendedor" de "Comprador", y
                // "Propietario" en la familia OTROS. SectionLabel es solo el respaldo genérico.
                string.IsNullOrWhiteSpace(s.Title) ? SectionLabel(s.SectionType) : s.Title,
                s.Status, s.Reasons)
            {
                SectionType = s.SectionType,
                SectionTypes = s.SectionTypes,
                SectionConfig = BuildSectionConfig(s, gateProfile),
            })
            .ToList();

        // CF-06 (HU #10881) — el override del OT es IO y vive fuera del evaluador puro; se compone
        // aquí para que el wizard y el gate de preparación devuelvan el mismo código de bloqueo.
        var blockers = result.Blockers;
        var canSubmit = result.CanSubmit;
        if (prendaOtBlocker is not null && !blockers.Contains(prendaOtBlocker, StringComparer.Ordinal))
        {
            blockers = [.. blockers, prendaOtBlocker];
            canSubmit = false;
        }

        return new WizardStateDto(
            instance.FamilyCode ?? string.Empty,
            instance.TypeCode,
            result.Steps.Count,
            wizardSteps,
            canSubmit,
            blockers,
            instance.Status,
            TramiteStateMachine.TransitionsFrom(instance.Status))
        {
            // ADR-0050 — el mismo perfil que acaba de gobernar los gates viaja al asistente, para
            // que no vuelva a deducir por familia lo que el tipo ya declara.
            TypeName = instance.TypeName,
            Capabilities = WizardCapabilitiesDto.From(conformation.GateProfile),
        };
    }

    /// <summary>
    /// Adjunta flags de instancia (paso persistido, subsanación) y filtra transiciones UI:
    /// <c>rechazado→entregado</c> no se expone como acción de transición (va por POST /submit
    /// con flag activo).
    /// </summary>
    private static WizardStateDto AnnotateInstanceFlags(WizardStateDto state, ProcedureInstance instance)
    {
        var allowed = TramiteStateMachine.TransitionsFrom(instance.Status)
            .Where(t => !(string.Equals(instance.Status, TramiteEstado.Rechazado, StringComparison.OrdinalIgnoreCase)
                          && t == TramiteEstado.Entregado))
            .ToList();

        return state with
        {
            PersistedCurrentStep = instance.CurrentStep,
            SubsanacionActiva = instance.SubsanacionActiva,
            SubsanacionCount = instance.SubsanacionCount,
            AllowedTransitions = allowed,
            Status = instance.Status,
            EsMigrado = instance.IsMigrated,
        };
    }

    /// <summary>
    /// Mapea las partes aprobadas (comprador/vendedor/locatario) a los códigos de entidad del
    /// gate_profile (BUYER/OWNER/LESSEE) para compararlas con <c>biometricActors</c>.
    /// </summary>
    /// <summary>
    /// CFD-06 — requisitos documentales del tipo, resueltos con los overrides del OT, en la forma que
    /// consume <see cref="DocumentRequirementGate"/>. Lista vacía si no hay proveedor cableado o el
    /// tipo no tiene matriz: el gate cae entonces al booleano agregado, sin regresión.
    /// </summary>
    private async Task<IReadOnlyList<DocumentRequirementItem>> ResolveDynamicDocumentRequirementsAsync(
        ProcedureInstance instance, CancellationToken ct)
    {
        if (_checklistMatrixProvider is null)
            return [];

        var matriz = await _checklistMatrixProvider
            .GetForAsync(
                instance.ProcedureTypeId,
                instance.TransitOfficeId ?? TransitOfficeIdFromFieldValues(instance),
                ct)
            .ConfigureAwait(false);

        return [.. matriz.Select(d => new DocumentRequirementItem(d.Codigo, d.Obligatorio, d.EsDummy))];
    }

    /// <summary>
    /// Configuración que el <c>SectionRendererRegistry</c> del frontend necesita para pintar la
    /// sección. La propiedad existía en el contrato desde F08 y nunca se asignaba, así que el cliente
    /// recibía siempre <c>null</c> y no podía saber, por ejemplo, si el paso de actores pide vendedor.
    /// </summary>
    private static JsonObject? BuildSectionConfig(
        DynamicWizardStepResult step, ProcedureTypeGateProfile profile)
    {
        switch (step.SectionType)
        {
            case ProcedureSectionTypes.VehicleQuery:
                return new JsonObject { ["entryMode"] = profile.EntryMode };

            case ProcedureSectionTypes.ActorForm:
                return new JsonObject
                {
                    ["requiresSeller"] = profile.RequiresSeller,
                    ["requiresBuyer"] = profile.RequiresBuyer,
                    ["allowsMultipleSeller"] = profile.AllowsMultipleSeller,
                    ["allowsMultipleBuyer"] = profile.AllowsMultipleBuyer,
                };

            case ProcedureSectionTypes.Commercial:
                return new JsonObject
                {
                    ["requiresCommercialValue"] = profile.RequiresCommercialValue,
                    ["commercialValueSource"] = profile.CommercialValueSource,
                };

            case ProcedureSectionTypes.Biometric:
                return new JsonObject
                {
                    ["requiresBiometrics"] = profile.RequiresBiometrics,
                    ["actors"] = new JsonArray([.. profile.BiometricActors.Select(a => (JsonNode)a!)]),
                };

            case ProcedureSectionTypes.SignatureFur:
                return new JsonObject { ["requiresSignature"] = profile.RequiresSignature };

            case ProcedureSectionTypes.PlateRequest:
                return new JsonObject { ["requiresPlateRequest"] = profile.RequiresPlateRequest };

            case ProcedureSectionTypes.PrendaDecision:
                return new JsonObject { ["hasPrendaGate"] = profile.HasPrendaGate };

            default:
                // document_checklist y generic_form no necesitan config: el checklist llega por su
                // propio endpoint y el genérico se pinta desde form_fields.
                return null;
        }
    }

    private static HashSet<string> MapPartiesToEntityCodes(IReadOnlySet<string> approvedParties)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (approvedParties.Contains("comprador")) codes.Add("BUYER");
        if (approvedParties.Contains("vendedor")) codes.Add("OWNER");
        if (approvedParties.Contains("locatario")) codes.Add("LESSEE");
        return codes;
    }

    private static bool PlateRequestCompleted(Dictionary<string, string?> fv) =>
        string.Equals(Get(fv, "plate_request_completed"), "true", StringComparison.OrdinalIgnoreCase);

    private static string SectionLabel(string sectionType) => sectionType switch
    {
        "vehicle_query" => "Consulta del vehículo",
        "document_checklist" => "Datos y Documentos del Trámite",
        "actor_form" => "Actores",
        "commercial" => "Valor comercial",
        "biometric" => "Identidad",
        "signature_fur" => "Firma / FUR",
        "plate_request" => "Solicitud de placa",
        "prenda_decision" => "Prenda",
        _ => "Datos",
    };

    /// <summary>Une comprador y vendedor al set aprobado (identidad deshabilitada, HU #10548).</summary>
    private static HashSet<string> IdentitySatisfiedForAllParties(IReadOnlySet<string> approved) =>
        new(approved, StringComparer.OrdinalIgnoreCase) { "comprador", "vendedor" };

    /// <summary>Id del OT elegido en el FUR (field_value <c>transit_office_id</c>), o null.</summary>
    private static Guid? TransitOfficeIdFromFieldValues(ProcedureInstance instance)
    {
        var raw = instance.FieldValues.FirstOrDefault(f =>
            string.Equals(f.FieldKey, "transit_office_id", StringComparison.OrdinalIgnoreCase))?.ValueText;
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Compañía+OT: ¿el documento de prenda es obligatorio y el trámite no lo tiene adjunto?
    /// Aplica a cualquier modalidad (matrícula, traspaso, etc.). Usa <c>TransitOfficeId</c> de la
    /// instancia (mismo OT que la matriz documental) y el <c>CreatedAt</c> para el snapshot.
    ///
    /// <para>2026-08-12 — se consulta también la DECISIÓN vigente. Antes no, y el resultado era un
    /// bloqueo insatisfacible: una matrícula inicial con <c>sin_prenda</c> quedaba atascada en
    /// "Finalizar" pidiendo un adjunto que el paso de prenda no ofrece cargar para esa decisión. La
    /// regla la impone <see cref="PrendaGate.EvaluateOtOverride"/>; aquí solo se le aporta el dato.</para>
    ///
    /// <para>Devuelve el CÓDIGO de bloqueo, no un booleano: el override puede pedir el documento
    /// (<c>prenda_documento_requerido_ot</c>) o la decisión que aún nadie tomó
    /// (<c>prenda_decision_requerida</c>), y el wizard tiene que decir cuál de las dos para que el
    /// banner coincida con lo que devuelve el gate de preparación.</para>
    /// </summary>
    private async Task<string?> PrendaOtBlockerAsync(ProcedureInstance instance, CancellationToken ct)
    {
        var otRequiere = await _prendaDocumentRequirementPolicy
            .IsRequiredAsync(
                instance.TenantId,
                instance.TransitOfficeId ?? TransitOfficeIdFromFieldValues(instance),
                instance.CreatedAt,
                ct)
            .ConfigureAwait(false);
        if (!otRequiere)
            return null;

        // Solo se paga la lectura del agregado de prenda cuando el override está activo: sin él la
        // decisión da igual y el gate no bloquea de todos modos.
        var prenda = _prendaRepo is null
            ? null
            : await _prendaRepo.GetVigenteAsync(instance.Id, instance.TenantId, ct).ConfigureAwait(false);

        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        return PrendaGate.EvaluateOtOverride(otRequiere, prenda?.Decision, docTipos);
    }

    /// <summary>
    /// Computa el estado server-driven del wizard a partir de una instancia con el grafo del wizard
    /// ya cargado (FieldValues, Actors, Attachments, Commercial, PreflightSnapshots, BiometricValidations,
    /// Signatures). Expuesto para reusar la MISMA lógica de gates por paso desde otros handlers (p.ej. el
    /// listado de trámites computa <c>PasoActual</c> contando los pasos en <c>complete</c>) sin duplicarla.
    /// </summary>
    /// <param name="documentosCompletosOverride">
    /// HU #10522 (RF17/RF22): completitud documental resuelta desde la matriz del gestor; <c>null</c>
    /// ⇒ se usa el cómputo actual del catálogo (flag OFF, sin matriz, o llamadores que no lo aportan,
    /// p. ej. el listado de trámites), sin regresión.
    /// </param>
    /// <param name="comparendosBloquean">
    /// FEATURE 05: si los comparendos bloquean el paso 4 de traspaso (criterio <c>fines</c> por
    /// compañía + OT). Default <c>true</c> ⇒ comportamiento previo; los llamadores que no resuelven la
    /// política (p. ej. el listado de trámites) lo dejan en el default sin regresión.
    /// </param>
    /// <param name="prendaOtBlocker">
    /// CF-06 (HU #10881): código de bloqueo que emite el override del OT
    /// (<c>prenda_documento_requerido_ot</c> o <c>prenda_decision_requerida</c>), o <c>null</c> si no
    /// bloquea. Default <c>null</c> ⇒ comportamiento previo (sin override, o llamadores que no lo
    /// resuelven, p. ej. el listado de trámites), sin regresión.
    /// </param>
    public static WizardStateDto ComputeState(
        ProcedureInstance instance,
        IReadOnlySet<string> identidadAprobadaPartes,
        bool? documentosCompletosOverride = null,
        bool comparendosBloquean = true,
        string? prendaOtBlocker = null,
        RuntConsultaExigida? runtExigido = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(identidadAprobadaPartes);

        var conformation = FromCatalog(instance);

        // Tipo sin pasos parametrizados. No debería ocurrir —los 21 del catálogo los tienen y hay un
        // test que lo verifica— pero un tipo creado a mano desde el configurador puede quedarse sin
        // ellos. Se devuelve un estado vacío y bloqueado en vez de inventar un recorrido: el
        // expediente se ve, no avanza, y el motivo es explícito.
        if (conformation is null)
        {
            // Sin pasos parametrizados no hay capacidades que publicar: el asistente pinta el
            // bloqueo, no un recorrido a medias.
            return new WizardStateDto(
                string.Empty, instance.TypeCode, 0, [], false, [TipoSinParametrizar],
                instance.Status, TramiteStateMachine.TransitionsFrom(instance.Status))
            {
                TypeName = instance.TypeName,
            };
        }

        // Migración V1→V2 — un trámite MIGRADO en estado terminal es una FOTO de solo lectura: no se
        // capturó paso a paso en V2, así que no se somete al gating vivo del wizard (que exige datos
        // —comercial, biométrica, FUR— que la migración legítimamente no reconstruye). Se reporta el
        // expediente como completo/solo-lectura para que el visor lo muestre íntegro. Reutiliza
        // TramiteEstado.EsFinal (aprobado/anulado = inmutable, RF04) como predicado de "terminal".
        if (instance.IsMigrated && TramiteEstado.EsFinal(instance.Status))
            return BuildReadonlySnapshot(instance, conformation);

        // ADR-0050 — mismo motor que el detalle del wizard, conformado desde el catálogo del tipo.
        // El listado no resuelve por IO la matriz documental ni la prenda (una consulta por fila):
        // el gate de documentos cae entonces a la completitud agregada, que es lo que este camino
        // usaba antes de todos modos.
        return BuildDynamicStateCore(
            instance, conformation, identidadAprobadaPartes, prendaOtBlocker,
            comparendosBloquean, runtExigido, documentRequirements: [], prendaVigente: null);
    }

    /// <summary>
    /// Migración V1→V2 — estado del wizard para un trámite MIGRADO en estado terminal: FOTO de solo
    /// lectura. Todos los pasos se reportan <c>complete</c> (sin reasons) para que el visor los muestre
    /// íntegros; <c>canSubmit=false</c> y <c>blockers=[]</c> porque un trámite terminal no admite acciones
    /// (<see cref="TramiteEstado.EsFinal"/>, RF04) y <see cref="TramiteStateMachine.TransitionsFrom"/> ya
    /// devuelve [] para aprobado/anulado. No evalúa gates: el expediente ya vino tal cual de V1 y no debe
    /// someterse al gating del flujo vivo (que exige comercial/biométrica/FUR inexistentes en la foto).
    /// </summary>
    /// <summary>
    /// Foto de solo lectura de un trámite migrado en estado terminal: todos los pasos del tipo en
    /// <c>complete</c> y sin acciones. Los pasos salen del catálogo, igual que en el camino vivo.
    /// </summary>
    private static WizardStateDto BuildReadonlySnapshot(
        ProcedureInstance instance, WizardConformation conformation)
    {
        var steps = conformation.Steps
            .Select((st, i) => new WizardStepDto(
                i + 1,
                st.StepCode,
                string.IsNullOrWhiteSpace(st.StepTitle) ? SectionLabel(st.PrimarySectionType) : st.StepTitle!,
                "complete",
                [])
            {
                SectionType = st.PrimarySectionType,
                SectionTypes = st.SectionTypes,
            })
            .ToList();

        return new WizardStateDto(
            string.Empty,
            instance.TypeCode,
            steps.Count,
            steps,
            false,   // canSubmit: terminal, sin acciones
            [],      // blockers
            instance.Status,
            TramiteStateMachine.TransitionsFrom(instance.Status))
        {
            TypeName = instance.TypeName,
            Capabilities = WizardCapabilitiesDto.From(conformation.GateProfile),
        };
    }

    // ---- Composición de canSubmit / blockers --------------------------------

    /// <summary>
    /// canSubmit = todos los pasos NO diferidos en complete + sin blockers globales.
    /// Preflight red NO se fuerza (queda como blocker explícito). Los pasos diferidos
    /// (biométrica/firma, slices 6-7) no cuentan contra el submit de este slice.
    /// </summary>
    private static bool CanSubmit(
        IReadOnlyList<WizardStepDto> steps,
        List<string> blockers,
        IReadOnlyList<int> deferredIndexes)
    {
        if (blockers.Count > 0)
            return false;

        foreach (var s in steps)
        {
            if (deferredIndexes.Contains(s.Index))
                continue;
            if (s.Status != "complete")
                return false;
        }

        return true;
    }

    /// <summary>
    /// Blockers globales que vetan el submit. Preflight con error de proveedor (consulta no
    /// verificable) → <c>preflight_provider_error</c>: bloqueo DURO, NO se levanta aceptando el
    /// riesgo (la información es vital). Preflight red subsanable → <c>preflight_red</c> (se levanta
    /// si el gestor aceptó el riesgo). + el gate borrador→preparado de N 03 (RF03): faltan
    /// obligatorios → <c>documentos_incompletos</c>; identidad del comprador no aprobada/vigente →
    /// <c>identidad_no_aprobada</c>. El blocker global es necesario porque en traspaso el paso 6
    /// (docs) es diferido y quedaría excluido del cómputo de pasos no-diferidos de <see cref="CanSubmit"/>.
    /// </summary>
    private static List<string> BlockersFrom(
        PreflightSnapshot? preflight,
        bool documentosCompletos,
        bool riesgoAceptado,
        bool identidadAprobada,
        string? prendaOtBlocker = null)
    {
        var blockers = new List<string>(5);
        if (preflight?.ProviderError == true)
            blockers.Add("preflight_provider_error");
        else if (preflight?.Overall == "red" && !riesgoAceptado)
            blockers.Add("preflight_red");
        if (!documentosCompletos)
            blockers.Add(TramiteEstadoErrores.DocumentosIncompletos);
        if (!identidadAprobada)
            blockers.Add(TramiteEstadoErrores.IdentidadNoAprobada);
        // CF-06 (HU #10881) — override del OT: exige el documento de prenda con independencia del
        // semáforo de gravámenes (sí de la decisión: ver PrendaGate.EvaluateOtOverride). Código
        // PROPIO para que el banner pueda decir que el origen es una regla del organismo y no la
        // decisión del gestor — el gate de preparación emite exactamente el mismo por este camino,
        // incluido el prenda_decision_requerida de cuando aún no hay decisión que evaluar.
        if (prendaOtBlocker is not null)
            blockers.Add(prendaOtBlocker);
        return blockers;
    }

    /// <summary>
    /// Resuelve la tipología (por <c>tipologia_codigo</c> o <c>modalidad_entrada</c>), computa el
    /// checklist sobre los adjuntos cargados y estado manual (igual que <see cref="GetChecklistHandler"/>)
    /// y devuelve si no faltan documentos obligatorios. Tipología no resoluble → true (sin checklist que exigir).
    /// </summary>
    private static bool DocumentosObligatoriosCompletos(ProcedureInstance instance)
    {
        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();

        var codigo = instance.TypeCode;

        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
    }

    // ---- Mapeo persistencia → contextos del dominio -------------------------

    private static Dictionary<string, string?> FieldValues(ProcedureInstance instance) =>
        instance.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Check preflight de la firma de la compraventa (paridad Johan <c>derivaFirmaCompraventaCheck</c>).
    /// Devuelve <c>null</c> si la tipología NO es traspaso_standard (no aplica firma de compraventa).
    /// <c>ok</c> (status green) cuando AMBAS partes están firmadas; <c>fail</c> (red) si alguna firma
    /// está rechazada; <c>warn</c> (yellow) en cualquier otro caso (pendiente de firmar).
    /// </summary>
    public static PreflightCheckDto? DerivaFirmaCompraventaCheck(ProcedureInstance instance)
    {
        var codigo = instance.TypeCode;
        if (!string.Equals(codigo, TramiteTipologiaCatalog.CodigoTraspasoStandard, StringComparison.OrdinalIgnoreCase))
            return null;

        var firmas = instance.Signatures
            .Where(s => string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var anyRechazada = firmas.Any(s => s.Estado == SignatureEstados.Rechazada);
        if (anyRechazada)
            return new PreflightCheckDto("firma_compraventa", "Firma compraventa", "fail", "firma",
                "Una de las partes rechazó la firma de la compraventa.");

        if (FirmaAmbasFirmadas(instance))
            return new PreflightCheckDto("firma_compraventa", "Firma compraventa", "green", "firma",
                "Ambas partes firmaron la compraventa.");

        return new PreflightCheckDto("firma_compraventa", "Firma compraventa", "warn", "firma",
            "Pendiente de firma de la compraventa.");
    }

    /// <summary>FUR generado = existe un adjunto del sistema con tipo 'fur' (Slice 7).</summary>
    private static bool FurGenerado(ProcedureInstance instance) =>
        instance.Attachments.Any(a => string.Equals(a.Tipo, "fur", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Firma de la compraventa completa: AMBAS partes (comprador + vendedor) tienen una firma
    /// <c>firmada</c> de la compraventa (Slice 7, solo traspaso).
    /// </summary>
    private static bool FirmaAmbasFirmadas(ProcedureInstance instance)
    {
        bool Firmada(string parte) => instance.Signatures.Any(s =>
            string.Equals(s.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.DocTipo, SignatureDocTipos.Compraventa, StringComparison.OrdinalIgnoreCase)
            && s.Estado == SignatureEstados.Firmada);

        return Firmada(SignatureRules.ParteComprador) && Firmada(SignatureRules.ParteVendedor);
    }

    private static ParteDatos? ParteOf(ProcedureInstance instance, string actorType)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
        if (a is null)
            return null;

        // HU #11593 — ciudad/dirección viven en actor.metadata (JSON); el teléfono en la columna.
        var (ciudad, direccion, _, _) = ActorMetadataReader.Parse(a.Metadata);
        return new ParteDatos(a.FullName, a.DocumentNumber, a.Email, ciudad, direccion, a.Phone);
    }

    /// <summary>
    /// RUNT se considera consultado cuando el actor existe con documento. En este slice el RUNT
    /// se hidrata en field_values (Slice 5) sin entidad propia; el documento del snapshot coincide
    /// con el del actor por construcción (el gate normaliza y compara documentos).
    /// </summary>
    /// <summary>
    /// Arma la exigencia de RUNT del trámite: los actores cuyo perfil de validación tiene
    /// <c>requiresRunt</c> y los documentos con consulta evidenciada. Devuelve
    /// <see cref="RuntConsultaExigida.Ninguna"/> si el tipo no exige RUNT a nadie, para no pagar la
    /// lectura de eventos cuando no aplica.
    /// </summary>
    private async Task<RuntConsultaExigida?> ResolveRuntExigidoAsync(
        ProcedureInstance instance, Guid tenantId, CancellationToken ct)
    {
        if (typeRepo is null)
            return null;

        var tipo = await typeRepo.GetByIdWithDetailsAsync(instance.ProcedureTypeId, ct).ConfigureAwait(false);
        if (tipo is null)
            return null;

        var actores = tipo.ConformationRules
            .Where(r => r.IsActive && RequiereRunt(r.ValidationProfile))
            .Select(r => RuntConsultaExigida.ActorTypeDeEntidad(r.ProcedureEntity?.Code))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        if (actores.Count == 0)
            return RuntConsultaExigida.Ninguna;

        var consultados = await repo
            .ListRuntConsultedDocumentKeysAsync(instance.Id, tenantId, ct)
            .ConfigureAwait(false);

        return new RuntConsultaExigida(actores, consultados);
    }

    /// <summary>¿El perfil de validación del actor marca <c>requiresRunt</c>?</summary>
    private static bool RequiereRunt(string? validationProfile)
    {
        if (string.IsNullOrWhiteSpace(validationProfile))
            return false;
        try
        {
            var node = JsonNode.Parse(validationProfile);
            return node?["requiresRunt"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Perfil ilegible: no se inventa una exigencia que el configurador no expresó.
            return false;
        }
    }

    private static RuntSnapshot? RuntOf(
        ProcedureInstance instance, string actorType, RuntConsultaExigida? exigencia = null)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
        if (a is null || string.IsNullOrWhiteSpace(a.DocumentNumber))
            return null;

        // Cuando el tipo de trámite marca este actor con requiresRunt, la consulta debe estar
        // EVIDENCIADA para el mismo documento; sin evidencia el gate del paso no pasa. Sin exigencia
        // configurada se conserva el comportamiento anterior (documento digitado ⇒ consultado).
        var consultado = exigencia?.Exige(actorType) != true
            || exigencia.FueConsultado(a.DocumentType, a.DocumentNumber);

        return new RuntSnapshot(Consultado: consultado, Documento: a.DocumentNumber);
    }

    /// <summary>
    /// SIMIT del comprador. Los comparendos se derivan del check SIMIT ESPECÍFICO del comprador
    /// (<c>simit_comprador*</c>) del último preflight, NO del <c>Overall</c> del vehículo (Bug #10728):
    /// el overall se pone rojo por SOAT/RTM/estado del vehículo, ajenos a las multas de la persona, así
    /// que inferir comparendos del overall producía un falso <c>simit_multas</c>. Sin preflight aún →
    /// null (el gate exige consulta SIMIT).
    ///
    /// FEATURE 05 — el gate se deriva de la CLAVE del check, no de su severidad. Los comparendos ya
    /// no pintan rojo (pasaron de <c>fail</c> a <c>warn</c> en <see cref="Consultations.FinesCheckFactory"/>:
    /// no bloquean CREAR el trámite), pero sí siguen bloqueando la RADICACIÓN al OT. Se aceptan ambas
    /// severidades: <c>warn</c> (actual) y <c>fail</c> (snapshots persistidos antes del cambio, que son
    /// JSON inmutable y hay que seguir leyendo bien).
    ///
    /// El sufijo <c>_multas</c> es OBLIGATORIO en la coincidencia:
    /// <c>simit_comprador_acuerdos_pago</c> TAMBIÉN es <c>warn</c> y NO es un comparendo — sin este
    /// guard, todo comprador con un acuerdo de pago activo dispararía un <c>simit_multas</c> falso.
    /// Y <c>unknown</c> (sin documento al correr el preflight) o <c>error</c> (proveedor caído, cuya
    /// clave es el prefijo pelado <c>simit_comprador</c>, bloqueo duro aparte vía ProviderError) NO se
    /// infieren como multas.
    /// </summary>
    private static SimitSnapshot? SimitOf(ProcedureInstance instance, ParteDatos? comprador, PreflightSnapshot? preflight)
    {
        if (comprador is null || string.IsNullOrWhiteSpace(comprador.Documento) || preflight is null)
            return null;

        var checks = LatestPreflightChecks(instance);
        var hasComparendos = checks.Any(c =>
            c.Key.StartsWith("simit_comprador", StringComparison.Ordinal) &&
            c.Key.EndsWith($"_{Consultations.FinesCheckFactory.KeyMultas}", StringComparison.Ordinal) &&
            (string.Equals(c.Status, "warn", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase)));

        return new SimitSnapshot(
            Consultado: true,
            Documento: comprador.Documento,
            TotalComparendos: hasComparendos ? 1 : 0);
    }

    private static PreflightSnapshot? PreflightOf(ProcedureInstance instance)
    {
        var latest = instance.PreflightSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        if (latest is null)
            return null;

        var checks = GetPreflightHandler.DeserializeChecks(latest.Checks);
        var impuestoUnknown = checks.Any(c =>
            c.Key.Contains("impuesto", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Status, "unknown", StringComparison.OrdinalIgnoreCase));
        var providerError = checks.Any(c => string.Equals(c.Status, "error", StringComparison.OrdinalIgnoreCase));
        // El vehículo no existe en el RUNT: la fuente respondió, pero no hay vehículo que tramitar.
        // Bloqueo DURO igual que providerError (ver PreflightSnapshot).
        var vehiculoNoEncontrado = checks.Any(c =>
            string.Equals(c.Key, "vehiculo", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase));
        return new PreflightSnapshot(latest.Overall, impuestoUnknown, providerError, vehiculoNoEncontrado);
    }

    /// <summary>Checks del último preflight (por fecha); lista vacía si aún no se ha corrido.</summary>
    private static IReadOnlyList<PreflightCheckDto> LatestPreflightChecks(ProcedureInstance instance)
    {
        var latest = instance.PreflightSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        return latest is null ? [] : GetPreflightHandler.DeserializeChecks(latest.Checks);
    }

    // Heurísticas de field_values (RUNT/VIN se hidratan en Slice 5).
    private static bool HasVehiculoConsulta(Dictionary<string, string?> fv) =>
        !string.IsNullOrWhiteSpace(Get(fv, "vin")) || !string.IsNullOrWhiteSpace(Get(fv, "plate"));

    private static bool PazSalvoVerificado(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var v = Get(fv, "paz_salvo_impuesto");
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// El gestor aceptó el riesgo de rechazo en el OT ante un preflight rojo subsanable
    /// (checkbox "Asumo el riesgo…"). Persistido en field_values como <c>riesgo_aceptado=true</c>.
    /// </summary>
    private static bool RiesgoAceptado(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var v = Get(fv, "riesgo_aceptado");
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    // ---- Etiquetas y keys de pasos ------------------------------------------

    /// <summary>
    /// HU #10879 — ¿la consulta del vehículo está completa? (VIN o placa hidratados en field_values).
    /// Es la MISMA señal que abre el paso 1 del wizard; se expone para el gate de "avanzar de paso"
    /// (AC1: solo se puede persistir el avance una vez consultado el vehículo). La instancia debe traer
    /// cargados sus <c>FieldValues</c>.
    /// </summary>
    public static bool VehiculoConsultado(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return HasVehiculoConsulta(FieldValues(instance));
    }

    /// <summary>
    /// HU #10879 — <c>Key</c>s ORDENADAS de los pasos del wizard estático para la modalidad de la
    /// instancia (matrícula 5 · traspaso 6). Fuente única para validar que el paso que se intenta
    /// persistir es uno legítimo del wizard, reusando el MISMO mapeo índice→key que expone el contrato.
    /// </summary>
    /// <summary>
    /// CF-02 (HU #10883, AC3) — esqueleto del wizard para el paso 1 cuando el trámite AÚN NO EXISTE.
    /// Mismos pasos, claves y etiquetas que el wizard real —salen del mismo catálogo— con el paso 1
    /// abierto y el resto bloqueado: la cascada de gates no es evaluable sin instancia, y el trámite
    /// se crea justo al avanzar al paso 2.
    /// <para>ADR-0050: se construye desde el TIPO, no desde la modalidad, así que el preview existe
    /// para cualquier tipo parametrizado y no solo para matrícula y traspaso.</para>
    /// </summary>
    /// <param name="type">Tipo con sus pasos y secciones cargados.</param>
    public static WizardStateDto? BuildPreview(ProcedureType? type)
    {
        if (type is null)
            return null;

        var conformation = FromCatalogSteps(type);
        if (conformation is null)
            return null;

        var steps = conformation.Steps
            .Select((st, i) => new WizardStepDto(
                i + 1,
                st.StepCode,
                string.IsNullOrWhiteSpace(st.StepTitle) ? SectionLabel(st.PrimarySectionType) : st.StepTitle!,
                i == 0 ? "incomplete" : "locked",
                [])
            {
                SectionType = st.PrimarySectionType,
                SectionTypes = st.SectionTypes,
                SectionConfig = BuildSectionConfig(
                    new DynamicWizardStepResult(i + 1, st.StepCode, st.PrimarySectionType, "locked", []),
                    conformation.GateProfile),
            })
            .ToList();

        return new WizardStateDto(
            string.Empty,
            type.Code,
            steps.Count,
            steps,
            CanSubmit: false,
            Blockers: [],
            TramiteEstado.Borrador,
            AllowedTransitions: [])
        {
            // El paso 1 ya necesita saber por qué identificador entra el vehículo: es la diferencia
            // entre pedir VIN o placa, y era lo primero que el asistente deducía de la modalidad.
            TypeName = type.Name,
            Capabilities = WizardCapabilitiesDto.From(conformation.GateProfile),
        };
    }

    /// <summary>
    /// HU #10879 — claves ORDENADAS de los pasos del wizard de la instancia, para validar que el paso
    /// que se intenta persistir es uno legítimo. Salen del catálogo del tipo (ADR-0050), que es la
    /// misma fuente que usa el motor: antes se derivaban de la modalidad y eran siempre 5 o 6.
    /// </summary>
    public static IReadOnlyList<string> StepKeysFor(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var conformation = FromCatalog(instance);
        return conformation is null ? [] : [.. conformation.Steps.Select(st => st.StepCode)];
    }
}
