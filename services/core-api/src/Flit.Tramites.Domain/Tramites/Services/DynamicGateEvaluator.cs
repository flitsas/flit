using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Un paso del wizard dinámico: su código y los <c>section_type</c> que lo componen (CFD-09).
/// <para>Un paso puede tener varias secciones — el DDL modela <c>procedure_sections</c> como N:1
/// contra <c>procedure_steps</c>. Antes solo se conservaba la primera y las demás desaparecían en
/// silencio, así que sus gates no se evaluaban.</para>
/// </summary>
public sealed record DynamicWizardStep(string StepCode, IReadOnlyList<string> SectionTypes)
{
    /// <summary>Paso de una sola sección — la forma habitual.</summary>
    public DynamicWizardStep(string stepCode, string sectionType)
        : this(stepCode, [sectionType]) { }

    /// <summary>
    /// Códigos de las secciones, en el mismo orden que <see cref="SectionTypes"/>. Los consume
    /// <c>actor_form</c> para saber a QUÉ actor corresponde el paso: el <c>section_type</c> por sí
    /// solo no lo dice, y un traspaso tiene dos pasos de actores (vendedor y comprador). Vacío =
    /// sin pista, y entonces la sección exige todos los actores que el perfil declare.
    /// </summary>
    public IReadOnlyList<string> SectionCodes { get; init; } = [];

    /// <summary>
    /// Título configurado del paso. Es lo que el operador lee, y por eso lo decide el catálogo y no
    /// el <c>section_type</c>: dos pasos <c>actor_form</c> del mismo trámite son "Vendedor" y
    /// "Comprador", y en la familia OTROS el mismo tipo de sección se presenta como "Propietario".
    /// Vacío = el cliente cae a la etiqueta genérica por tipo de sección.
    /// </summary>
    public string? StepTitle { get; init; }

    /// <summary>
    /// Renderer principal del paso: la primera sección. Es lo que viaja en el contrato como
    /// <c>sectionType</c>; el detalle completo va en <c>sectionTypes</c>.
    /// </summary>
    public string PrimarySectionType =>
        SectionTypes.Count > 0 ? SectionTypes[0] : Flit.Tramites.Domain.Enums.ProcedureSectionTypes.GenericForm;
}

/// <summary>Resultado de un paso del wizard dinámico.</summary>
public sealed record DynamicWizardStepResult(
    int Index,
    string Key,
    string SectionType,
    string Status,   // complete | incomplete | locked
    IReadOnlyList<string> Reasons)
{
    /// <summary>Título configurado del paso; <c>null</c> si el catálogo no lo define.</summary>
    public string? Title { get; init; }

    /// <summary>Todas las secciones del paso, en orden. El frontend renderiza esta lista completa.</summary>
    public IReadOnlyList<string> SectionTypes { get; init; } = [SectionType];
}

/// <summary>Estado del wizard dinámico computado por <see cref="DynamicGateEvaluator"/>.</summary>
public sealed record DynamicWizardState(
    IReadOnlyList<DynamicWizardStepResult> Steps,
    bool CanSubmit,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Señales de estado de la instancia necesarias para evaluar los gates del wizard dinámico. El
/// handler de aplicación las extrae del grafo de la instancia (actores, field_values, preflight,
/// comercial, biométricas, firmas, checklist) — la MISMA extracción del camino estático — y las pasa
/// aquí para mantener paridad. Campos <c>bool?</c> = señal no evaluable (no bloquea).
/// </summary>
public sealed record DynamicWizardContext
{
    public bool VehiculoConsultado { get; init; }
    public bool PreflightProviderError { get; init; }
    /// <summary>El RUNT respondió y el vehículo NO existe: bloqueo DURO, igual que
    /// <see cref="PreflightProviderError"/> (ver PreflightSnapshot.VehiculoNoEncontrado).</summary>
    public bool PreflightVehiculoNoEncontrado { get; init; }
    public bool DocumentosCompletos { get; init; }
    public bool HasBuyer { get; init; }
    public bool BuyerRuntConsultado { get; init; }
    public bool HasSeller { get; init; }
    public bool SellerRuntConsultado { get; init; }

    /// <summary>
    /// La parte tiene los seis datos de contacto que exige <see cref="ParteCompletaRule"/> (HU #11593).
    /// Existir y tener RUNT no basta: sin contacto el organismo no puede notificar ni radicar.
    /// </summary>
    public bool BuyerCompleto { get; init; } = true;

    /// <inheritdoc cref="BuyerCompleto"/>
    public bool SellerCompleto { get; init; } = true;
    public decimal ValorVenta { get; init; }
    /// <summary>Códigos de actor con identidad/biometría aprobada (e.g. BUYER, OWNER).</summary>
    public IReadOnlySet<string> BiometricsApproved { get; init; } = new HashSet<string>();
    public bool FurGenerado { get; init; }
    public bool PlateRequestCompleted { get; init; }
    /// <summary>Documentos requeridos (para el gate de documentos y sus blockers).</summary>
    public IReadOnlyList<DocumentRequirementItem> DocumentRequirements { get; init; } = [];
    public IReadOnlySet<string> UploadedDocumentCodes { get; init; } = new HashSet<string>();

    /// <summary>
    /// Decisión de prenda vigente de la instancia, para la sección <c>prenda_decision</c>. <c>null</c>
    /// = sin decisión registrada, que es justo lo que el gate bloquea cuando hay prenda que resolver.
    /// </summary>
    public ProcedureInstancePrenda? PrendaVigente { get; init; }

    /// <summary>
    /// El RUNT reportó un gravamen o una prenda sobre ESTE vehículo (<c>runt_tiene_prendas</c> /
    /// <c>runt_tiene_gravamenes</c> en field_values, que hidratan los tres mappers de consulta).
    ///
    /// <para>Es el disparador real de la decisión de prenda, en lugar de una marca del tipo. La
    /// prenda no es una propiedad del TIPO de trámite —un traspaso no gestiona prenda por ser
    /// traspaso— sino un hecho del VEHÍCULO: la gestiona si ese carro tiene una.</para>
    ///
    /// <para>Y es dato de la INSTANCIA, no del catálogo, así que no viaja en el snapshot congelado
    /// del tipo: un expediente ya abierto lo evalúa con lo que el RUNT respondió, sin quedarse con
    /// una copia vieja de la configuración.</para>
    /// </summary>
    public bool RuntReportaGravamen { get; init; }

    /// <summary>
    /// <c>code</c> del tipo con el que se conformó el expediente. Lo necesita la regla de prenda para
    /// reconocer los trámites que SON el gravamen (inscribir / levantar / cambiar acreedor), donde el
    /// disparador no puede ser lo que el RUNT reporte.
    /// </summary>
    public string? TypeCode { get; init; }

    /// <summary>
    /// Familia del tipo (<c>MATRICULAS</c> / <c>TRASPASO</c> / <c>OTROS</c>). La necesita la regla de
    /// prenda para saber si el expediente ACUMULA un gravamen sobre el tipo base: en OTROS el cambio
    /// ES el trámite (ADR-0050), así que un gravamen que el RUNT reporte no es asunto suyo y exigir
    /// una decisión dejaba el trámite sin salida.
    ///
    /// <para>Ausente (<c>null</c>) se trata como acumulable, el fail-safe de
    /// <see cref="ProcedureTypeLayers.FamiliaAcumulaComplementarios"/>: preserva el comportamiento
    /// previo en vez de apagar la prenda en silencio.</para>
    /// </summary>
    public string? FamilyCode { get; init; }

    /// <summary>Tipos de adjunto cargados; <see cref="PrendaGate"/> verifica contra ellos el
    /// documento que exige la decisión de prenda.</summary>
    public IReadOnlyCollection<string> AttachmentTipos { get; init; } = [];

    /// <summary>El comprador tiene comparendos SIMIT pendientes.</summary>
    public bool CompradorConComparendos { get; init; }

    /// <summary>Semáforo del pre-vuelo en rojo.</summary>
    public bool PreflightRed { get; init; }

    /// <summary>El gestor aceptó el riesgo del semáforo rojo, que deja de bloquear la radicación.</summary>
    public bool RiesgoAceptado { get; init; }

    /// <summary>
    /// FEATURE 05 — ¿los comparendos vetan el avance? La compañía puede marcarlos informativos para
    /// el OT destino. Default <c>true</c>, el comportamiento previo a esa feature.
    /// </summary>
    public bool ComparendosBloquean { get; init; } = true;
}

/// <summary>
/// Evaluador dinámico PURO (sin IO) del wizard (FEATURE-08 / CFD-09). Compone los gates ya construidos
/// (<see cref="ProcedureTypeGateProfile"/>, <see cref="DocumentRequirementGate"/>,
/// <see cref="PlateRequestGate"/>) para computar el estado de cada paso según su <c>section_type</c>,
/// más <c>canSubmit</c> y los <c>blockers</c> globales. NO modifica los gates estáticos
/// (MatriculaGates/TraspasoGates/PrendaGate): reproduce su semántica desde la configuración del tipo.
/// </summary>
public static class DynamicGateEvaluator
{
    // Reasons/blockers alineados con el camino estático (WizardStateQuery / SubmitGate).
    public const string VehiculoNoConsultado = "vehiculo_no_consultado";

    /// <summary>Paridad con <c>MatriculaGates</c>: entrada por VIN sin consultar.</summary>
    public const string VinPendiente = "vin_pendiente";

    /// <summary>Paridad con <c>TraspasoGates</c>: entrada por placa sin consultar.</summary>
    public const string ConsultaPendiente = "consulta_pendiente";

    /// <summary>Paridad con <c>WizardStateQuery.BlockersFrom</c>: pre-vuelo en rojo sin riesgo aceptado.</summary>
    public const string PreflightRedBlocker = "preflight_red";
    public const string PreflightProviderError = "preflight_provider_error";
    public const string VehiculoNoEncontrado = "vehiculo_no_encontrado";
    public const string DocumentosIncompletos = "documentos_incompletos";
    public const string CompradorPendiente = "comprador_pendiente";
    public const string VendedorPendiente = "vendedor_pendiente";
    public const string ValorComercialPendiente = "valor_comercial_pendiente";
    public const string PendienteBiometria = "pendiente_biometria";

    /// <summary>Paridad con el estático: acompaña a <see cref="PendienteBiometria"/> en el paso de identidad.</summary>
    public const string IdentidadPendiente = "identidad_pendiente";
    public const string FurPendiente = "fur_pendiente";
    public const string IdentidadNoAprobada = "identidad_no_aprobada";

    /// <summary>Paridad con <c>TraspasoGates</c>: el comprador tiene comparendos SIMIT pendientes.</summary>
    public const string SimitMultas = "simit_multas";

    public static DynamicWizardState Evaluate(
        ProcedureTypeGateProfile profile,
        IReadOnlyList<DynamicWizardStep> steps,
        DynamicWizardContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<DynamicWizardStepResult>(steps.Count);
        var index = 1;
        foreach (var step in steps)
        {
            // El paso está completo solo si TODAS sus secciones lo están; las razones se acumulan
            // sin duplicar (dos secciones pueden fallar por el mismo motivo).
            var status = "complete";
            var reasons = new List<string>();
            for (var si = 0; si < step.SectionTypes.Count; si++)
            {
                var sectionType = step.SectionTypes[si];
                var sectionCode = si < step.SectionCodes.Count ? step.SectionCodes[si] : null;
                var (sectionStatus, sectionReasons) = EvaluateSection(sectionType, sectionCode, profile, context);
                if (sectionStatus != "complete")
                    status = sectionStatus;
                foreach (var reason in sectionReasons)
                {
                    if (!reasons.Contains(reason, StringComparer.Ordinal))
                        reasons.Add(reason);
                }
            }

            results.Add(new DynamicWizardStepResult(
                index, StepKey(step, index), step.PrimarySectionType, status, reasons)
            {
                SectionTypes = step.SectionTypes,
                Title = step.StepTitle,
            });
            index++;
        }

        results = ApplyDeferredLocks(results);

        var blockers = GlobalBlockers(profile, context);
        var canSubmit = ComputeCanSubmit(results, blockers);

        return new DynamicWizardState(results, canSubmit, blockers);
    }

    /// <summary>
    /// <c>canSubmit</c> a nivel de radicación (SubmitGate delega aquí para tipos dinámicos, AC-06):
    /// blockers globales derivados del perfil + estado. Vacío ⇒ puede radicar.
    /// </summary>
    public static IReadOnlyList<string> CanSubmitBlockers(
        ProcedureTypeGateProfile profile, DynamicWizardContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        return GlobalBlockers(profile, context);
    }

    private static (string Status, IReadOnlyList<string> Reasons) EvaluateSection(
        string sectionType, string? sectionCode, ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        var reasons = new List<string>();

        // Bloqueos DUROS del pre-vuelo: no son exclusivos del paso de consulta. El camino estático los
        // repite en el gate de documentos (MatriculaGates paso 3) porque sin vehículo verificado no se
        // avanza, y no se levantan aceptando el riesgo. Los pasos diferidos quedan fuera: su bloqueo
        // lo gobierna la completitud de los datos.
        if (!IsDeferred(sectionType) && sectionType != Flit.Tramites.Domain.Enums.ProcedureSectionTypes.VehicleQuery)
        {
            if (ctx.PreflightProviderError)
                return Incomplete(reasons, PreflightProviderError);
            if (ctx.PreflightVehiculoNoEncontrado)
                return Incomplete(reasons, VehiculoNoEncontrado);
            if (ctx.PreflightRed && !ctx.RiesgoAceptado)
                return Incomplete(reasons, PreflightRedBlocker);
        }

        switch (sectionType)
        {
            case "vehicle_query":
                if (ctx.PreflightProviderError)
                    return Incomplete(reasons, PreflightProviderError);
                if (ctx.PreflightVehiculoNoEncontrado)
                    return Incomplete(reasons, VehiculoNoEncontrado);
                if (ctx.VehiculoConsultado)
                    return Complete();
                // El código de la razón depende del modo de entrada, igual que en el camino estático:
                // el operador necesita saber QUÉ identificador falta, no solo que falta la consulta.
                // Se emite también el genérico para no romper a quien ya escuchaba ese código.
                reasons.Add(EntryModeReason(profile));
                reasons.Add(VehiculoNoConsultado);
                return ("incomplete", reasons);

            case "document_checklist":
                // Además del código agregado se emiten los DOCUMENT_{CODE}_REQUIRED de CFD-06: el gate
                // ya los calculaba y se descartaban, así que el cliente no podía decir qué falta.
                var faltantes = MissingDocumentCodes(ctx);
                if (faltantes.Count > 0)
                {
                    reasons.Add(DocumentosIncompletos);
                    reasons.AddRange(faltantes);
                    return ("incomplete", reasons);
                }
                return DocumentosOk(profile, ctx) ? Complete() : Incomplete(reasons, DocumentosIncompletos);

            case "actor_form":
                // El código de la sección acota el paso a su actor. Sin él (secciones sin nombrar) se
                // conserva el comportamiento previo: el paso exige todos los actores del perfil.
                var pideVendedor = profile.RequiresSeller && SectionCoversSeller(sectionCode);
                var pideComprador = profile.RequiresBuyer && SectionCoversBuyer(sectionCode);
                if (pideVendedor && (!ctx.HasSeller || !ctx.SellerRuntConsultado || !ctx.SellerCompleto))
                    reasons.Add(VendedorPendiente);
                if (pideComprador && (!ctx.HasBuyer || !ctx.BuyerRuntConsultado || !ctx.BuyerCompleto))
                    reasons.Add(CompradorPendiente);
                // Paridad con TraspasoGates: los comparendos solo vetan si la compañía los marcó
                // bloqueantes para el OT destino; si son informativos, quedan en el preflight.
                if (pideComprador && ctx.CompradorConComparendos && ctx.ComparendosBloquean)
                    reasons.Add(SimitMultas);
                return reasons.Count == 0 ? Complete() : ("incomplete", reasons);

            case "commercial":
                if (profile.RequiresCommercialValue && ctx.ValorVenta <= 0m)
                    return Incomplete(reasons, ValorComercialPendiente);
                return Complete();

            case "biometric":
                if (BiometricsOk(profile, ctx))
                    return Complete();
                // Paridad con el estático, que emite las dos: 'identidad_pendiente' es la que pinta el
                // banner y 'pendiente_biometria' la que detalla el motivo.
                reasons.Add(IdentidadPendiente);
                reasons.Add(PendienteBiometria);
                return ("incomplete", reasons);

            case "signature_fur":
                if (profile.RequiresSignature && !ctx.FurGenerado)
                    return Incomplete(reasons, FurPendiente);
                return ctx.FurGenerado ? Complete() : Incomplete(reasons, FurPendiente);

            case "plate_request":
                var plate = PlateRequestGate.Evaluate(profile, ctx.PlateRequestCompleted);
                return plate.Ok ? Complete() : Incomplete(reasons, PlateRequestGate.PlateRequestPending);

            case "prenda_decision":
                // R10, disparado por lo que HAY que resolver —el tipo es de prenda, o el RUNT reportó
                // un gravamen sobre un expediente que puede acumularlo— y no por una marca del tipo.
                // Ver ProcedureTypeLayers.ExigeDecisionDePrenda.
                if (!ProcedureTypeLayers.ExigeDecisionDePrenda(
                        profile, ctx.FamilyCode, ctx.TypeCode, ctx.RuntReportaGravamen))
                    return Complete();
                var prendaError = PrendaGate.EvaluateDecision(ctx.PrendaVigente, ctx.AttachmentTipos);
                return prendaError is null ? Complete() : Incomplete(reasons, prendaError);

            case "generic_form":
            default:
                // Los form_fields se validan en su propia sección; el paso genérico no bloquea aquí.
                return Complete();
        }
    }

    /// <summary>
    /// Códigos <c>DOCUMENT_{CODE}_REQUIRED</c> de los requisitos obligatorios sin cargar. Vacío si el
    /// tipo no tiene matriz configurada — ahí solo aplica el booleano agregado.
    /// </summary>
    private static IReadOnlyList<string> MissingDocumentCodes(DynamicWizardContext ctx) =>
        ctx.DocumentRequirements.Count == 0
            ? []
            : DocumentRequirementGate.MissingRequired(ctx.DocumentRequirements, ctx.UploadedDocumentCodes);

    // Convención de códigos de sección para los pasos de actores. Es configuración del tipo, no una
    // heurística sobre la familia del trámite: el DDL nombra las secciones VENDEDOR / COMPRADOR.
    private static bool SectionCoversSeller(string? sectionCode) =>
        sectionCode is null
        || sectionCode.Contains("VENDEDOR", StringComparison.OrdinalIgnoreCase)
        || sectionCode.Contains("SELLER", StringComparison.OrdinalIgnoreCase)
        || sectionCode.Contains("OWNER", StringComparison.OrdinalIgnoreCase);

    private static bool SectionCoversBuyer(string? sectionCode) =>
        sectionCode is null
        || sectionCode.Contains("COMPRADOR", StringComparison.OrdinalIgnoreCase)
        || sectionCode.Contains("BUYER", StringComparison.OrdinalIgnoreCase);

    private static string EntryModeReason(ProcedureTypeGateProfile profile) =>
        string.Equals(profile.EntryMode, ProcedureTypeGateProfile.EntryModeVin, StringComparison.OrdinalIgnoreCase)
            ? VinPendiente
            : ConsultaPendiente;

    private static bool DocumentosOk(ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        if (ctx.DocumentRequirements.Count > 0)
            return DocumentRequirementGate.MissingRequired(ctx.DocumentRequirements, ctx.UploadedDocumentCodes).Count == 0;
        return ctx.DocumentosCompletos;
    }

    private static bool BiometricsOk(ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        if (!profile.RequiresBiometrics)
            return true;
        foreach (var actor in profile.BiometricActors)
        {
            if (!ctx.BiometricsApproved.Contains(actor))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Blockers de la radicación borrador→preparado, en paridad con
    /// <c>WizardStateQuery.BlockersFrom</c>.
    /// <para>El FUR NO aparece aquí a propósito: es un paso diferido y no veta la radicación (se
    /// genera al validar la identidad, HU #10349). Incluirlo hacía que un expediente con todos los
    /// datos listos no pudiera radicar.</para>
    /// </summary>
    private static List<string> GlobalBlockers(ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        var blockers = new List<string>();
        if (ctx.PreflightProviderError)
            blockers.Add(PreflightProviderError);
        else if (ctx.PreflightRed && !ctx.RiesgoAceptado)
            blockers.Add(PreflightRedBlocker);
        if (ctx.PreflightVehiculoNoEncontrado)
            blockers.Add(VehiculoNoEncontrado);
        if (!DocumentosOk(profile, ctx))
        {
            blockers.Add(DocumentosIncompletos);
            blockers.AddRange(MissingDocumentCodes(ctx));
        }
        if (profile.RequiresBiometrics && !BiometricsOk(profile, ctx))
            blockers.Add(IdentidadNoAprobada);
        // El blocker GLOBAL de prenda: es el que pinta la franja «Requisitos pendientes antes del
        // envío» y deshabilita Finalizar. Un trámite sin dimensión de prenda no lo emite aunque el
        // RUNT reporte un gravamen sobre el vehículo: el prevuelo lo señala, pero no hay nada que
        // este expediente pueda decidir al respecto.
        if (ProcedureTypeLayers.ExigeDecisionDePrenda(
                profile, ctx.FamilyCode, ctx.TypeCode, ctx.RuntReportaGravamen))
        {
            var prendaError = PrendaGate.EvaluateDecision(ctx.PrendaVigente, ctx.AttachmentTipos);
            if (prendaError is not null)
                blockers.Add(prendaError);
        }
        return blockers;
    }

    /// <summary>Puede radicar si no hay blockers globales y todos los pasos no diferidos están completos.</summary>
    private static bool ComputeCanSubmit(IReadOnlyList<DynamicWizardStepResult> steps, List<string> blockers)
    {
        if (blockers.Count > 0)
            return false;
        foreach (var s in steps)
        {
            // Los pasos de identidad/firma son diferidos (igual que en el camino estático): no cuentan
            // contra el submit — su faltante ya está en blockers globales cuando el tipo los exige.
            if (s.SectionType is "biometric" or "signature_fur")
                continue;
            if (s.Status != "complete")
                return false;
        }
        return true;
    }

    private static (string, IReadOnlyList<string>) Complete() => ("complete", []);

    private static (string, IReadOnlyList<string>) Incomplete(List<string> reasons, string code)
    {
        reasons.Add(code);
        return ("incomplete", reasons);
    }

    /// <summary>
    /// Bloqueo de los pasos DIFERIDOS (identidad y firma), en paridad con
    /// <c>WizardStateQuery.BuildMatricula</c>: se marcan <c>locked</c> mientras los pasos de datos no
    /// estén completos y, a partir de ahí, muestran su estado real. Es el desacople de HU #10350 — el
    /// gestor recorre hasta el último paso y finaliza el borrador aunque la identidad siga pendiente.
    /// <para>Los pasos de DATOS <b>no</b> se bloquean en cascada: el camino estático evalúa cada uno
    /// con su propio gate y los deja <c>incomplete</c>, de modo que el operador ve todo lo que le
    /// falta y no solo lo primero.</para>
    /// </summary>
    private static List<DynamicWizardStepResult> ApplyDeferredLocks(List<DynamicWizardStepResult> steps)
    {
        var firstIncompleteData = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            if (IsDeferred(steps[i].SectionType))
                continue;
            if (steps[i].Status != "complete")
            {
                firstIncompleteData = i;
                break;
            }
        }

        var datosCompletos = firstIncompleteData < 0;

        var result = new List<DynamicWizardStepResult>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (IsDeferred(step.SectionType))
            {
                result.Add(datosCompletos ? step : step with { Status = "locked", Reasons = [] });
                continue;
            }

            result.Add(step);
        }

        return result;
    }

    /// <summary>Pasos que el gestor puede alcanzar aunque estén pendientes (HU #10350).</summary>
    private static bool IsDeferred(string sectionType) =>
        sectionType is Flit.Tramites.Domain.Enums.ProcedureSectionTypes.Biometric
                    or Flit.Tramites.Domain.Enums.ProcedureSectionTypes.SignatureFur;

    private static string StepKey(DynamicWizardStep step, int index) =>
        string.IsNullOrWhiteSpace(step.StepCode) ? $"paso_{index}" : step.StepCode;
}
