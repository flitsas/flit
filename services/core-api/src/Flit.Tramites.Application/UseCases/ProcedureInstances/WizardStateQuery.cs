using Flit.Tramites.Domain.Entities;
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
    IReadOnlyList<string> Reasons);

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
/// comercial → ValorVenta;
/// checklist (<see cref="ChecklistEngine"/> sobre adjuntos) → completitud de documentos del paso 2;
/// biométrica → <c>null</c> (diferida, slice 6) → los pasos finales se marcan incomplete con
/// reason explícita ("pendiente_biometria"/"pendiente_firma"), NO se bloquean con error.</para>
/// </summary>
public sealed class GetWizardStateHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationPolicy? identityPolicy = null,
    ChecklistMatrixCompleteness? matrixCompleteness = null)
{
    public const string PendienteBiometria = "pendiente_biometria";
    public const string PendienteFirma = "pendiente_firma";
    public const string FurPendiente = "fur_pendiente";

    // HU #10548 — política de exigibilidad de identidad por OT (default permisivo en tests).
    private readonly IIdentityValidationPolicy _identityPolicy =
        identityPolicy ?? NullIdentityValidationPolicy.Instance;

    public async Task<(WizardStateDto? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // Identidad PER-PERSONA (documento del actor), no por instancia: se referencia la validación
        // vigente de la persona en N trámites sin clonar (HU #10350).
        var identidadAprobada = await IdentityApprovalResolver.ResolveApprovedPartiesAsync(
            repo, instance, DateTimeOffset.UtcNow, ct);

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

        var state = ComputeState(instance, partesEfectivas, docsCompletos) with
        {
            IdentityValidationEnabled = identityRequired,
        };
        return (state, null);
    }

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
    public static WizardStateDto ComputeState(
        ProcedureInstance instance,
        IReadOnlySet<string> identidadAprobadaPartes,
        bool? documentosCompletosOverride = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(identidadAprobadaPartes);

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
                        ?? TramiteModalidadEntrada.MatriculaInicial;

        return modalidad == TramiteModalidadEntrada.Traspaso
            ? BuildTraspaso(instance, identidadAprobadaPartes, documentosCompletosOverride)
            : BuildMatricula(instance, identidadAprobadaPartes, documentosCompletosOverride);
    }

    // ---- Matrícula inicial (5 pasos) ----------------------------------------

    private static WizardStateDto BuildMatricula(
        ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes, bool? docsCompletosOverride = null)
    {
        var fv = FieldValues(instance);
        var comprador = ParteOf(instance, "comprador");
        var runtComprador = RuntOf(instance, "comprador");
        var preflight = PreflightOf(instance);

        var docsCompletos = docsCompletosOverride ?? DocumentosObligatoriosCompletos(instance);
        var riesgoAceptado = RiesgoAceptado(instance);

        // Matrícula: la única parte (comprador) lleva la biométrica. Aprobación PER-PERSONA (documento),
        // no por instancia: se referencia la identidad vigente de la persona en N trámites (HU #10350).
        var identidadAprobada = identidadAprobadaPartes.Contains("comprador");

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

        // N 03 (RF03): canSubmit/blockers reflejan el gate borrador→preparado — identidad del
        // comprador aprobada/vigente + documentos obligatorios. El FUR/firma (paso 5, slice 7)
        // sigue diferido. El frontend nunca recalcula gates: solo pinta estos códigos.
        var blockers = BlockersFrom(preflight, docsCompletos, riesgoAceptado, identidadAprobada);
        var canSubmit = CanSubmit(steps, blockers, deferredIndexes: [4, 5]);

        return new WizardStateDto(
            TramiteModalidadEntradaCodes.MatriculaInicial,
            instance.TipologiaCodigo,
            MatriculaGates.TotalPasos,
            steps,
            canSubmit,
            blockers,
            instance.Status,
            TramiteStateMachine.TransitionsFrom(instance.Status));
    }

    // ---- Traspaso estándar (6 pasos) ----------------------------------------

    private static WizardStateDto BuildTraspaso(
        ProcedureInstance instance, IReadOnlySet<string> identidadAprobadaPartes, bool? docsCompletosOverride = null)
    {
        var fv = FieldValues(instance);
        var vendedor = ParteOf(instance, "vendedor");
        var comprador = ParteOf(instance, "comprador");
        var runtVendedor = RuntOf(instance, "vendedor");
        var runtComprador = RuntOf(instance, "comprador");
        var preflight = PreflightOf(instance);
        var simitComprador = SimitOf(instance, comprador, preflight);
        var docsCompletos = docsCompletosOverride ?? DocumentosObligatoriosCompletos(instance);
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
            // Biométrica real (slice 6): traspaso requiere ambas partes. Aprobación PER-PERSONA (documento),
            // referenciada de la identidad vigente de cada persona (HU #10350), no por instancia.
            Biometria = new BiometriaSnapshot(
                Vendedor: identidadAprobadaPartes.Contains("vendedor"),
                Comprador: identidadAprobadaPartes.Contains("comprador")),
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
                // Paso 6 = Generar FUR: biométrica de AMBAS partes (slice 6) + FUR generado. Los
                // documentos ya se exigen en el paso 2 (paridad con matrícula); aquí NO se listan
                // como reason. El faltante de docs sigue vetando el submit vía el blocker global
                // documentos_incompletos.
                //
                // B12 (HU #10661, ADR-0028): la firma de compraventa YA NO condiciona el completado
                // del paso 6 ni aporta `pendiente_firma` — negocio aún no define la lógica ideal de
                // firmas. El estado de firma queda informativo en el preflight `firma_compraventa`
                // (DerivaFirmaCompraventaCheck, warn/green), sin bloquear canSubmit.
                var biometriaOk = TraspasoGates.GateFur(ctx.Biometria, ctx.ForzarContinuar).Ok;
                var furOk = FurGenerado(instance);

                if (!biometriaOk)
                    reasons.Add(PendienteBiometria);
                if (!furOk)
                    reasons.Add(FurPendiente);

                status = (biometriaOk && furOk) ? "complete" : "incomplete";
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

        // N 03 (RF03): mismo gate de preparación que matrícula — la identidad exigida es la del
        // comprador (endurecer vendedor+firma en traspaso sigue como deuda M5, ver SubmitGate).
        var blockers = BlockersFrom(preflight, docsCompletos, riesgoAceptado, ctx.Biometria.Comprador);
        var canSubmit = CanSubmit(steps, blockers, deferredIndexes: [6]);

        return new WizardStateDto(
            TramiteModalidadEntradaCodes.Traspaso,
            instance.TipologiaCodigo,
            TraspasoGates.TotalPasos,
            steps,
            canSubmit,
            blockers,
            instance.Status,
            TramiteStateMachine.TransitionsFrom(instance.Status));
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
        bool identidadAprobada)
    {
        var blockers = new List<string>(4);
        if (preflight?.ProviderError == true)
            blockers.Add("preflight_provider_error");
        else if (preflight?.Overall == "red" && !riesgoAceptado)
            blockers.Add("preflight_red");
        if (!documentosCompletos)
            blockers.Add(TramiteEstadoErrores.DocumentosIncompletos);
        if (!identidadAprobada)
            blockers.Add(TramiteEstadoErrores.IdentidadNoAprobada);
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
        return new PreflightSnapshot(latest.Overall, impuestoUnknown, providerError);
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
