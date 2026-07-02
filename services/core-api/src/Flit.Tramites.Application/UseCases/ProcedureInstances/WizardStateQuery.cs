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

        return (ComputeState(instance), null);
    }

    /// <summary>
    /// Computa el estado server-driven del wizard a partir de una instancia con el grafo del wizard
    /// ya cargado (FieldValues, Actors, Attachments, Commercial, PreflightSnapshots, BiometricValidations,
    /// Signatures). Expuesto para reusar la MISMA lógica de gates por paso desde otros handlers (p.ej. el
    /// listado de trámites computa <c>PasoActual</c> contando los pasos en <c>complete</c>) sin duplicarla.
    /// </summary>
    public static WizardStateDto ComputeState(ProcedureInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        return modalidad == TramiteModalidadEntrada.Traspaso
            ? BuildTraspaso(instance)
            : BuildMatricula(instance);
    }

    // ---- Matrícula inicial (5 pasos) ----------------------------------------

    private static WizardStateDto BuildMatricula(ProcedureInstance instance)
    {
        var fv = FieldValues(instance);
        var comprador = ParteOf(instance, "comprador");
        var runtComprador = RuntOf(instance, "comprador");
        var preflight = PreflightOf(instance);

        var docsCompletos = DocumentosObligatoriosCompletos(instance);
        var riesgoAceptado = RiesgoAceptado(instance);

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
            RiesgoPreflightAceptado = riesgoAceptado,
        };

        var maxAlcanzable = MatriculaGates.MaxPasoAlcanzable(ctx);
        // Datos (pasos 1-3) completos: maxAlcanzable >= 4 ⇒ Consulta+Documentos+Comprador OK. A partir
        // de aquí los pasos diferidos (4 Identidad, 5 FUR) son ALCANZABLES aunque la identidad esté
        // pendiente — desacople HU #10350: el gestor recorre hasta el último paso y finaliza el borrador.
        var datosCompletos = maxAlcanzable >= 4;
        var pasos = TipologiaMatrizCatalog.Get(TramiteTipologiaCatalog.CodigoMatriculaInicial)?.Pasos
                    ?? [];

        var steps = new List<WizardStepDto>(MatriculaGates.TotalPasos);
        for (var p = 1; p <= MatriculaGates.TotalPasos; p++)
        {
            var reasons = new List<string>();
            string status;

            // 4 = Identidad (biométrica, slice 6): refleja el estado real de la biométrica del comprador.
            if (p == 4)
            {
                if (!datosCompletos)
                {
                    status = "locked";
                }
                else if (identidadAprobada)
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
            // 5 = Generar FUR (slice 7): diferido. Alcanzable en cuanto los datos están completos AUNQUE
            // la identidad siga pendiente (HU #10350), para que sea el ÚLTIMO paso del wizard donde el
            // gestor finaliza/radica. El FUR se genera automáticamente al validar la identidad (#10349).
            else if (p == 5)
            {
                if (!datosCompletos)
                {
                    status = "locked";
                }
                else if (FurGenerado(instance))
                {
                    status = "complete";
                }
                else
                {
                    status = "incomplete";
                    reasons.Add(FurPendiente);
                }
            }
            else
            {
                // Pasos de datos 1-3: cascada estándar por gate (no alcanzable ⇒ locked).
                var gate = MatriculaGates.PasoCompleto(p, ctx);
                if (p > maxAlcanzable)
                {
                    status = "locked";
                }
                else if (gate.Ok)
                {
                    status = "complete";
                }
                else
                {
                    status = "incomplete";
                    if (gate.Code is not null)
                        reasons.Add(gate.Code);
                }
            }

            steps.Add(new WizardStepDto(p, StepKey(false, p), StepLabel(pasos, p), status, reasons));
        }

        var blockers = BlockersFrom(preflight, docsCompletos, riesgoAceptado);
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
        var riesgoAceptado = RiesgoAceptado(instance);

        var ctx = new TraspasoGateContext
        {
            // El trámite ya existe (instancia creada) → radicado a efectos del wizard server-side.
            TramiteRadicado = true,
            // Consulta del vehículo por placa: sin ella el paso 1 queda incompleto (frontera),
            // así un traspaso recién creado abre en el paso 1 y no en el 2.
            VehiculoConsultado = HasVehiculoConsulta(fv),
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
            RiesgoPreflightAceptado = riesgoAceptado,
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

            // Flujo en cascada: un paso aún NO alcanzable (p > maxAlcanzable) se bloquea
            // (locked, sin reasons), incluido el diferido (6 FUR). El sidebar no deja saltar
            // a FUR sin haber completado los pasos previos. Solo si es alcanzable se evalúa.
            if (p > maxAlcanzable)
            {
                status = "locked";
            }
            // 6 = Generar FUR: docs obligatorios + biométrica de AMBAS partes (slice 6) +
            // firma de AMBAS partes (slice 7) + FUR generado. Completa solo cuando todo está listo;
            // emite las razones precisas de lo que falta.
            else if (p == 6)
            {
                // Paso 6 = Generar FUR: biométrica de AMBAS partes (slice 6) + firma de AMBAS
                // partes (slice 7) + FUR generado. Los documentos ya se exigen en el paso 2
                // (paridad con matrícula); aquí NO se listan como reason. El faltante de docs
                // sigue vetando el submit vía el blocker global documentos_incompletos.
                var biometriaOk = TraspasoGates.GateFur(ctx.Biometria, ctx.ForzarContinuar).Ok;
                var firmaOk = FirmaAmbasFirmadas(instance);
                var furOk = FurGenerado(instance);

                if (!biometriaOk)
                    reasons.Add(PendienteBiometria);
                if (!firmaOk)
                    reasons.Add(PendienteFirma);
                if (!furOk)
                    reasons.Add(FurPendiente);

                status = (biometriaOk && firmaOk && furOk) ? "complete" : "incomplete";
            }
            else if (gate.Ok)
            {
                status = "complete";
            }
            else
            {
                // Alcanzable (p <= maxAlcanzable) pero con gate sin cumplir → incomplete.
                status = "incomplete";
                if (gate.Code is not null)
                    reasons.Add(gate.Code);
            }

            steps.Add(new WizardStepDto(p, StepKey(true, p), StepLabel(pasos, p), status, reasons));
        }

        var blockers = BlockersFrom(preflight, docsCompletos, riesgoAceptado);
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
    /// Blockers globales que vetan el submit. Preflight con error de proveedor (consulta no
    /// verificable) → <c>preflight_provider_error</c>: bloqueo DURO, NO se levanta aceptando el
    /// riesgo (la información es vital). Preflight red subsanable → <c>preflight_red</c> (se levanta
    /// si el gestor aceptó el riesgo). + gating ESTRICTO de documentos obligatorios: faltan
    /// obligatorios → <c>documentos_incompletos</c> (sin override). El blocker global es necesario
    /// porque en traspaso el paso 6 (docs) es diferido y quedaría excluido del cómputo de pasos
    /// no-diferidos de <see cref="CanSubmit"/>.
    /// </summary>
    private static List<string> BlockersFrom(
        PreflightSnapshot? preflight,
        bool documentosCompletos,
        bool riesgoAceptado)
    {
        var blockers = new List<string>(3);
        if (preflight?.ProviderError == true)
            blockers.Add("preflight_provider_error");
        else if (preflight?.Overall == "red" && !riesgoAceptado)
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
    private static bool BiometriaAprobada(ProcedureInstance instance, string? parte)
    {
        // HU #10350 — la validación cuenta como aprobada sólo si además está VIGENTE (≤30 días desde la
        // aprobación) Y corresponde al DOCUMENTO del actor actual de la parte. El doc-match es defensa en
        // profundidad: si el gestor cambió de persona y la invalidación de la validación previa no corrió
        // (p.ej. el ensure del frontend falló), el gate NO la cuenta como identidad de la persona actual.
        var now = DateTimeOffset.UtcNow;
        var actor = instance.Actors.FirstOrDefault(a =>
            string.Equals(a.ActorType, parte, StringComparison.OrdinalIgnoreCase));
        return instance.BiometricValidations.Any(v =>
            string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            && BiometricRules.EsAprobadaVigente(v, now)
            && BiometricRules.DocumentoCoincide(v, actor?.DocumentType, actor?.DocumentNumber));
    }

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
        // aquí reportamos 0 comparendos salvo que el overall sea red (multas → bloqueo). Un red por
        // ERROR de proveedor (consulta no verificable) NO implica comparendos: no se infiere.
        var totalComparendos = preflight.Overall == "red" && !preflight.ProviderError ? 1 : 0;
        return new SimitSnapshot(Consultado: true, Documento: comprador.Documento, TotalComparendos: totalComparendos);
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
        return new PreflightSnapshot(latest.Overall, impuestoUnknown, providerError);
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

    private static string StepLabel(IReadOnlyList<PasoTipologia> pasos, int index) =>
        pasos.FirstOrDefault(p => p.Paso == index)?.Titulo ?? $"Paso {index}";

    private static string StepKey(bool traspaso, int index) =>
        traspaso
            ? index switch
            {
                1 => "consulta",
                2 => "documentos",
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
