using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Un paso del wizard, en la forma congelada del contrato.</summary>
public sealed record WizardStepDto(
    int Index,
    string Key,
    string Label,
    string Status,           // complete | incomplete | locked
    IReadOnlyList<string> Reasons);

/// <summary>Estado server-driven del wizard, derivado de los gates del dominio.</summary>
public sealed record WizardStateDto(
    string Modalidad,
    string? TipologiaCodigo,
    int TotalSteps,          // 5 matrícula | 6 traspaso
    IReadOnlyList<WizardStepDto> Steps,
    bool CanSubmit,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Compone el estado del wizard server-driven. Carga el grafo persistido, lo mapea a los
/// contextos tipados del dominio (<see cref="MatriculaGateContext"/>/<see cref="TraspasoGateContext"/>),
/// invoca los gates por paso (fuente única de verdad) y traduce a status + reasons.
///
/// <para><b>Mapeo persistencia → GateContext:</b>
/// actores → <see cref="ParteDatos"/> + <see cref="RuntSnapshot"/> (RUNT se asume consultado
/// cuando el actor tiene documento; el RUNT vive en field_values, sin entidad propia en este slice);
/// último preflight → <see cref="PreflightSnapshot"/> (Overall);
/// comercial → ValorVenta;
/// checklist (<see cref="ChecklistEngine"/> sobre adjuntos) → completitud de documentos del paso 2;
/// biométrica → <c>null</c> (diferida, slice 6) → los pasos finales se marcan incomplete con
/// reason explícita ("pendiente_biometria"/"pendiente_firma"), NO se bloquean con error.</para>
/// </summary>
public sealed class GetWizardStateHandler(IProcedureInstanceRepository repo)
{
    public const string PendienteBiometria = "pendiente_biometria";
    public const string PendienteFirma = "pendiente_firma";
    public const string FurPendiente = "fur_pendiente";

    public async Task<(WizardStateDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        var dto = modalidad == TramiteModalidadEntrada.Traspaso
            ? BuildTraspaso(instance)
            : BuildMatricula(instance);

        return (dto, null);
    }

    // ---- Matrícula inicial (5 pasos) ----------------------------------------

    private static WizardStateDto BuildMatricula(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var comprador = ParteOf(instance, "comprador");
        var runtComprador = RuntOf(instance, "comprador");
        var preflight = PreflightOf(instance);

        var docsCompletos = DocumentosObligatoriosCompletos(instance);

        // Matrícula: la única parte (comprador) lleva la biométrica → Parte == "comprador".
        var identidadAprobada = BiometriaAprobada(instance, "comprador");

        var ctx = new MatriculaGateContext
        {
            VehiculoConsultado = HasVehiculoConsulta(fv),
            Preflight = preflight,
            Comprador = comprador,
            RuntComprador = runtComprador,
            IdentidadAprobada = identidadAprobada,
            DocumentosObligatoriosCompletos = docsCompletos,
            ForzarContinuar = false,
        };

        var maxAlcanzable = MatriculaGates.MaxPasoAlcanzable(ctx);
        var pasos = TipologiaMatrizCatalog.Get(TramiteTipologiaCatalog.CodigoMatriculaInicial)?.Pasos
                    ?? [];

        var steps = new List<WizardStepDto>(MatriculaGates.TotalPasos);
        for (var p = 1; p <= MatriculaGates.TotalPasos; p++)
        {
            var gate = MatriculaGates.PasoCompleto(p, ctx);
            var reasons = new List<string>();
            string status;

            // 4 = Identidad (biométrica, slice 6): refleja el estado real de la biométrica del comprador.
            // 5 = Generar FUR (firma, slice 7): diferido → incomplete con reason, sin bloqueo.
            if (p == 4)
            {
                if (identidadAprobada)
                {
                    status = "complete";
                }
                else
                {
                    status = "incomplete";
                    reasons.Add("identidad_pendiente");
                    reasons.Add(PendienteBiometria);
                }
            }
            else if (p == 5)
            {
                // FUR (Slice 7): matrícula NO requiere firma; completa cuando el FUR está generado.
                if (FurGenerado(instance))
                {
                    status = "complete";
                }
                else
                {
                    status = "incomplete";
                    reasons.Add(FurPendiente);
                }
            }
            else if (gate.Ok)
            {
                status = "complete";
            }
            else
            {
                status = p > maxAlcanzable ? "locked" : "incomplete";
                if (gate.Code is not null)
                    reasons.Add(gate.Code);
            }

            steps.Add(new WizardStepDto(p, StepKey(false, p), StepLabel(pasos, p), status, reasons));
        }

        var blockers = BlockersFrom(preflight, docsCompletos);
        // Identidad (paso 4) refleja el estado real de la biométrica (slice 6) en su status/reasons,
        // pero NO se cuenta como bloqueo duro del submit de este slice (paridad con Johan: la
        // biométrica se valida en el flujo FUR, no veta el radicado de datos). El FUR/firma (paso 5,
        // slice 7) sigue diferido. Si la identidad ya está aprobada, el paso queda complete sin reason.
        var canSubmit = CanSubmit(steps, blockers, deferredIndexes: [4, 5]);

        return new WizardStateDto(
            TramiteModalidadEntradaCodes.MatriculaInicial,
            instance.TipologiaCodigo,
            MatriculaGates.TotalPasos,
            steps,
            canSubmit,
            blockers);
    }

    // ---- Traspaso estándar (6 pasos) ----------------------------------------

    private static WizardStateDto BuildTraspaso(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var vendedor = ParteOf(instance, "vendedor");
        var comprador = ParteOf(instance, "comprador");
        var runtVendedor = RuntOf(instance, "vendedor");
        var runtComprador = RuntOf(instance, "comprador");
        var preflight = PreflightOf(instance);
        var simitComprador = SimitOf(instance, comprador, preflight);
        var docsCompletos = DocumentosObligatoriosCompletos(instance);

        var ctx = new TraspasoGateContext
        {
            // El trámite ya existe (instancia creada) → radicado a efectos del wizard server-side.
            TramiteRadicado = true,
            Preflight = preflight,
            PazSalvoImpuestoVerificado = PazSalvoVerificado(instance),
            Vendedor = vendedor,
            RuntVendedor = runtVendedor,
            Comprador = comprador,
            RuntComprador = runtComprador,
            SimitComprador = simitComprador,
            ValorVenta = instance.Commercial?.ValorVenta ?? 0m,
            // Biométrica real (slice 6): traspaso requiere ambas partes (comprador + vendedor).
            Biometria = new BiometriaSnapshot(
                Vendedor: BiometriaAprobada(instance, "vendedor"),
                Comprador: BiometriaAprobada(instance, "comprador")),
            DocumentosObligatoriosCompletos = docsCompletos,
            ForzarContinuar = false,
        };

        var maxAlcanzable = TraspasoGates.MaxPasoAlcanzable(ctx);
        var pasos = TipologiaMatrizCatalog.Get(TramiteTipologiaCatalog.CodigoTraspasoStandard)?.Pasos
                    ?? [];

        var steps = new List<WizardStepDto>(TraspasoGates.TotalPasos);
        for (var p = 1; p <= TraspasoGates.TotalPasos; p++)
        {
            var gate = TraspasoGates.PasoCompleto(p, ctx);
            var reasons = new List<string>();
            string status;

            // 6 = Generar FUR: docs obligatorios + biométrica de AMBAS partes (slice 6) +
            // firma de AMBAS partes (slice 7) + FUR generado. Completa solo cuando todo está listo;
            // emite las razones precisas de lo que falta.
            if (p == 6)
            {
                var biometriaOk = TraspasoGates.GateFur(ctx.Biometria, ctx.ForzarContinuar).Ok;
                var firmaOk = FirmaAmbasFirmadas(instance);
                var furOk = FurGenerado(instance);

                if (!docsCompletos)
                    reasons.Add("documentos_incompletos");
                if (!biometriaOk)
                    reasons.Add(PendienteBiometria);
                if (!firmaOk)
                    reasons.Add(PendienteFirma);
                if (!furOk)
                    reasons.Add(FurPendiente);

                status = (docsCompletos && biometriaOk && firmaOk && furOk) ? "complete" : "incomplete";
            }
            else if (gate.Ok)
            {
                status = "complete";
            }
            else
            {
                status = p > maxAlcanzable ? "locked" : "incomplete";
                if (gate.Code is not null)
                    reasons.Add(gate.Code);
            }

            steps.Add(new WizardStepDto(p, StepKey(true, p), StepLabel(pasos, p), status, reasons));
        }

        var blockers = BlockersFrom(preflight, docsCompletos);
        var canSubmit = CanSubmit(steps, blockers, deferredIndexes: [6]);

        return new WizardStateDto(
            TramiteModalidadEntradaCodes.Traspaso,
            instance.TipologiaCodigo,
            TraspasoGates.TotalPasos,
            steps,
            canSubmit,
            blockers);
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
    /// Blockers globales que vetan el submit. Preflight red (subsanable) + gating ESTRICTO de
    /// documentos obligatorios: faltan obligatorios → <c>documentos_incompletos</c> (sin override).
    /// El blocker global es necesario porque en traspaso el paso 6 (docs) es diferido y quedaría
    /// excluido del cómputo de pasos no-diferidos de <see cref="CanSubmit"/>.
    /// </summary>
    private static List<string> BlockersFrom(PreflightSnapshot? preflight, bool documentosCompletos)
    {
        var blockers = new List<string>(2);
        if (preflight?.Overall == "red")
            blockers.Add("preflight_red");
        if (!documentosCompletos)
            blockers.Add("documentos_incompletos");
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

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);

        var computed = ChecklistEngine.Compute(codigo, manual, docTipos);
        return computed?.Completo ?? true;
    }

    // ---- Mapeo persistencia → contextos del dominio -------------------------

    private static Dictionary<string, string?> FieldValues(ProcedureInstance instance) =>
        instance.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Biométrica aprobada para una parte. Matrícula usa la parte explícita <c>"comprador"</c> (única
    /// parte); traspaso usa <c>"comprador"</c>/<c>"vendedor"</c>. Aprobada = existe una validación de
    /// esa parte en estado <c>aprobado</c>.
    /// </summary>
    private static bool BiometriaAprobada(ProcedureInstance instance, string? parte) =>
        instance.BiometricValidations.Any(v =>
            string.Equals(v.Parte, parte, StringComparison.OrdinalIgnoreCase)
            && v.Estado == BiometricEstados.Aprobado);

    /// <summary>
    /// Check preflight de la firma de la compraventa (paridad Johan <c>derivaFirmaCompraventaCheck</c>).
    /// Devuelve <c>null</c> si la tipología NO es traspaso_standard (no aplica firma de compraventa).
    /// <c>ok</c> (status green) cuando AMBAS partes están firmadas; <c>fail</c> (red) si alguna firma
    /// está rechazada; <c>warn</c> (yellow) en cualquier otro caso (pendiente de firmar).
    /// </summary>
    public static PreflightCheckDto? DerivaFirmaCompraventaCheck(ProcedureInstance instance)
    {
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
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
        return a is null ? null : new ParteDatos(a.FullName, a.DocumentNumber, a.Email);
    }

    /// <summary>
    /// RUNT se considera consultado cuando el actor existe con documento. En este slice el RUNT
    /// se hidrata en field_values (Slice 5) sin entidad propia; el documento del snapshot coincide
    /// con el del actor por construcción (el gate normaliza y compara documentos).
    /// </summary>
    private static RuntSnapshot? RuntOf(ProcedureInstance instance, string actorType)
    {
        var a = instance.Actors.FirstOrDefault(x =>
            string.Equals(x.ActorType, actorType, StringComparison.OrdinalIgnoreCase));
        if (a is null || string.IsNullOrWhiteSpace(a.DocumentNumber))
            return null;
        return new RuntSnapshot(Consultado: true, Documento: a.DocumentNumber);
    }

    /// <summary>
    /// SIMIT del comprador. Si el último preflight es green/yellow (no red por multas), se considera
    /// consultado sin comparendos. Sin preflight aún → null (el gate exige consulta SIMIT).
    /// </summary>
    private static SimitSnapshot? SimitOf(ProcedureInstance instance, ParteDatos? comprador, PreflightSnapshot? preflight)
    {
        if (comprador is null || string.IsNullOrWhiteSpace(comprador.Documento) || preflight is null)
            return null;

        // El preflight ya corrió SIMIT del comprador. red por multas se refleja vía Overall;
        // aquí reportamos 0 comparendos salvo que el overall sea red (multas → bloqueo).
        var totalComparendos = preflight.Overall == "red" ? 1 : 0;
        return new SimitSnapshot(Consultado: true, Documento: comprador.Documento, TotalComparendos: totalComparendos);
    }

    private static PreflightSnapshot? PreflightOf(ProcedureInstance instance)
    {
        var latest = instance.PreflightSnapshots
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        if (latest is null)
            return null;

        var impuestoUnknown = ImpuestoUnknown(latest.Checks);
        return new PreflightSnapshot(latest.Overall, impuestoUnknown);
    }

    private static bool ImpuestoUnknown(string? checksJson) =>
        GetPreflightHandler.DeserializeChecks(checksJson)
            .Any(c => c.Key.Contains("impuesto", StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(c.Status, "unknown", StringComparison.OrdinalIgnoreCase));

    // Heurísticas de field_values (RUNT/VIN se hidratan en Slice 5).
    private static bool HasVehiculoConsulta(Dictionary<string, string?> fv) =>
        !string.IsNullOrWhiteSpace(Get(fv, "vin")) || !string.IsNullOrWhiteSpace(Get(fv, "plate"));

    private static bool PazSalvoVerificado(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var v = Get(fv, "paz_salvo_impuesto");
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Get(Dictionary<string, string?> fv, string key) =>
        fv.TryGetValue(key, out var v) ? v : null;

    // ---- Etiquetas y keys de pasos ------------------------------------------

    private static string StepLabel(IReadOnlyList<PasoTipologia> pasos, int index) =>
        pasos.FirstOrDefault(p => p.Paso == index)?.Titulo ?? $"Paso {index}";

    private static string StepKey(bool traspaso, int index) =>
        traspaso
            ? index switch
            {
                1 => "consulta",
                2 => "validacion",
                3 => "vendedor",
                4 => "comprador",
                5 => "comercial",
                6 => "fur",
                _ => $"paso_{index}",
            }
            : index switch
            {
                1 => "consulta_vin",
                2 => "documentos",
                3 => "comprador",
                4 => "identidad",
                5 => "fur",
                _ => $"paso_{index}",
            };
}
