namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>Un paso del wizard dinámico: su código y el <c>section_type</c> que lo renderiza (CFD-09).</summary>
public sealed record DynamicWizardStep(string StepCode, string SectionType);

/// <summary>
/// Estado de un actor requerido por el tipo, para el gate <c>actor_form</c> genérico (BE-06). Se deriva
/// de una regla de conformación (por-actor), NO de flags buyer/seller del gate_profile: aplica a
/// OWNER/BUYER/SELLER/LESSEE y a "múltiples propietarios" en CUALQUIER trámite, no solo traspaso.
/// <paramref name="EntityCode"/> es el código de entidad (BUYER/OWNER/…). <paramref name="Present"/> =
/// hay al menos un actor de ese tipo capturado. <paramref name="RequiresRunt"/> viene del
/// <c>validation_profile</c> de la regla. La multiplicidad (<c>allowsMultiple</c>) no bloquea: solo
/// permite añadir más; un actor requerido necesita ≥1 presente igual.
/// </summary>
public sealed record ActorGateState(
    string EntityCode,
    bool Present,
    bool RuntConsultado,
    bool RequiresRunt);

/// <summary>Resultado de un paso del wizard dinámico.</summary>
public sealed record DynamicWizardStepResult(
    int Index,
    string Key,
    string SectionType,
    string Status,   // complete | incomplete | locked
    IReadOnlyList<string> Reasons);

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
    public bool DocumentosCompletos { get; init; }
    /// <summary>
    /// Actores requeridos por el tipo (uno por regla de conformación no-VEHICLE), con su estado de
    /// captura/RUNT. Conduce el gate <c>actor_form</c> de forma genérica — cualquier actor en cualquier
    /// tipo, incluidos varios propietarios (OWNER con <c>allowsMultiple</c>).
    /// </summary>
    public IReadOnlyList<ActorGateState> Actors { get; init; } = [];
    public decimal ValorVenta { get; init; }
    /// <summary>Códigos de actor con identidad/biometría aprobada (e.g. BUYER, OWNER).</summary>
    public IReadOnlySet<string> BiometricsApproved { get; init; } = new HashSet<string>();
    public bool FurGenerado { get; init; }
    public bool PlateRequestCompleted { get; init; }
    /// <summary>Documentos requeridos (para el gate de documentos y sus blockers).</summary>
    public IReadOnlyList<DocumentRequirementItem> DocumentRequirements { get; init; } = [];
    public IReadOnlySet<string> UploadedDocumentCodes { get; init; } = new HashSet<string>();
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
    public const string PreflightProviderError = "preflight_provider_error";
    public const string DocumentosIncompletos = "documentos_incompletos";
    public const string CompradorPendiente = "comprador_pendiente";
    public const string VendedorPendiente = "vendedor_pendiente";
    public const string ValorComercialPendiente = "valor_comercial_pendiente";
    public const string PendienteBiometria = "pendiente_biometria";
    public const string FurPendiente = "fur_pendiente";
    public const string IdentidadNoAprobada = "identidad_no_aprobada";

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
            var (status, reasons) = EvaluateSection(step.SectionType, profile, context);
            results.Add(new DynamicWizardStepResult(
                index, StepKey(step, index), step.SectionType, status, reasons));
            index++;
        }

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
        string sectionType, ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        var reasons = new List<string>();
        switch (sectionType)
        {
            case "vehicle_query":
                if (ctx.PreflightProviderError)
                    return Incomplete(reasons, PreflightProviderError);
                return ctx.VehiculoConsultado ? Complete() : Incomplete(reasons, VehiculoNoConsultado);

            case "document_checklist":
                return DocumentosOk(profile, ctx) ? Complete() : Incomplete(reasons, DocumentosIncompletos);

            case "actor_form":
                // Genérico: cada actor requerido (una regla de conformación por-actor) debe estar
                // presente y, si su regla exige RUNT, consultado. No hay sesgo buyer/seller: OWNER,
                // LESSEE y "múltiples propietarios" en cualquier tipo se validan por igual.
                foreach (var actor in ctx.Actors)
                {
                    if (!actor.Present || (actor.RequiresRunt && !actor.RuntConsultado))
                        reasons.Add(ActorPendingReason(actor.EntityCode));
                }
                return reasons.Count == 0 ? Complete() : ("incomplete", reasons);

            case "commercial":
                if (profile.RequiresCommercialValue && ctx.ValorVenta <= 0m)
                    return Incomplete(reasons, ValorComercialPendiente);
                return Complete();

            case "biometric":
                return BiometricsOk(profile, ctx) ? Complete() : Incomplete(reasons, PendienteBiometria);

            case "signature_fur":
                if (profile.RequiresSignature && !ctx.FurGenerado)
                    return Incomplete(reasons, FurPendiente);
                return ctx.FurGenerado ? Complete() : Incomplete(reasons, FurPendiente);

            case "plate_request":
                var plate = PlateRequestGate.Evaluate(profile, ctx.PlateRequestCompleted);
                return plate.Ok ? Complete() : Incomplete(reasons, PlateRequestGate.PlateRequestPending);

            case "prenda_decision":
                // La decisión de prenda tiene componente específico; sin señal de decisión se deja
                // como incompleto informativo cuando el tipo la exige (hasPrendaGate).
                return profile.HasPrendaGate ? Complete() : Complete();

            case "generic_form":
            default:
                // Los form_fields se validan en su propia sección; el paso genérico no bloquea aquí.
                return Complete();
        }
    }

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

    private static List<string> GlobalBlockers(ProcedureTypeGateProfile profile, DynamicWizardContext ctx)
    {
        var blockers = new List<string>();
        if (ctx.PreflightProviderError)
            blockers.Add(PreflightProviderError);
        if (!DocumentosOk(profile, ctx))
            blockers.Add(DocumentosIncompletos);
        if (profile.RequiresBiometrics && !BiometricsOk(profile, ctx))
            blockers.Add(IdentidadNoAprobada);
        if (profile.RequiresSignature && !ctx.FurGenerado)
            blockers.Add(FurPendiente);
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

    private static string StepKey(DynamicWizardStep step, int index) =>
        string.IsNullOrWhiteSpace(step.StepCode) ? $"paso_{index}" : step.StepCode;

    /// <summary>
    /// Código de reason por actor faltante. Mantiene paridad con el camino estático para los actores
    /// comunes (BUYER→<c>comprador_pendiente</c>, OWNER→<c>vendedor_pendiente</c> — en FLIT la parte
    /// vendedora es la entidad OWNER) y usa un código genérico <c>actor_pendiente:{CODE}</c> para el
    /// resto (LESSEE, coopropietarios, etc.), de modo que cualquier actor en cualquier tipo tenga reason.
    /// </summary>
    private static string ActorPendingReason(string entityCode) =>
        (entityCode ?? string.Empty).ToUpperInvariant() switch
        {
            "BUYER" => CompradorPendiente,
            "OWNER" => VendedorPendiente,
            "" => "actor_pendiente",
            var code => $"actor_pendiente:{code}",
        };
}
