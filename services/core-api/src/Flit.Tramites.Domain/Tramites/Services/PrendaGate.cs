using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// R10 (HU #10597) — gate PURO de la decisión de prenda: exige una decisión de prenda vigente y, si la
/// decisión requiere documento (solicitar/registrar/levantar), su adjunto (y el acreedor cuando la
/// decisión constituye gravamen, HU #11591). <c>omitir</c>/<c>sin_prenda</c> satisfacen el gate sin
/// documento (<c>omitir</c> = la vía "asumo el riesgo"). Devuelve el código de error o <c>null</c> si
/// puede avanzar.
///
/// <para><b>Dos disparadores comparten este núcleo, sin duplicar la regla:</b> <see cref="Evaluate"/>
/// (traspaso, solo con el semáforo de gravámenes en <c>warn</c>) y
/// <see cref="EvaluateMatriculaInicial"/> (HU #11592, matrícula inicial, INCONDICIONAL — bloqueo duro
/// que INVIERTE deliberadamente la HU #10596, que dejaba la prenda de matrícula como declaración
/// puramente informativa sin gate). La detección del <c>warn</c> y la carga de la prenda son IO (viven
/// en el servicio de ciclo de vida); aquí solo la regla, para poder probarla sin cablear todo el
/// trámite.</para>
/// </summary>
public static class PrendaGate
{
    public static string? Evaluate(
        bool esTraspaso,
        bool hasGravamenWarn,
        ProcedureInstancePrenda? prendaVigente,
        IReadOnlyCollection<string> docTipos)
    {
        if (!esTraspaso || !hasGravamenWarn)
            return null;

        return EvaluateNucleo(prendaVigente, docTipos);
    }

    /// <summary>
    /// R10 aplicado a matrícula inicial (HU #11592) — mismo núcleo que <see cref="Evaluate"/> pero
    /// SIN el early-exit de traspaso/semáforo: en matrícula el gravamen se CONSTITUYE (vehículo nuevo
    /// financiado), no se detecta sobre un vehículo con historial, así que el semáforo de gravámenes
    /// del preflight no es significativo aquí — mismo razonamiento que ya documenta
    /// <see cref="EvaluateOtOverride"/> para el override del OT. Bloqueo INCONDICIONAL: sin decisión
    /// vigente registrada, no se puede preparar el trámite.
    /// </summary>
    public static string? EvaluateMatriculaInicial(
        ProcedureInstancePrenda? prendaVigente,
        IReadOnlyCollection<string> docTipos)
        => EvaluateDecision(prendaVigente, docTipos);

    /// <summary>
    /// El núcleo de R10 sin disparador: exige decisión vigente y, si la decisión lo requiere, su
    /// documento y acreedor. Lo consume la sección <c>prenda_decision</c> del motor dinámico
    /// (ADR-0050), donde el disparador ya no es la modalidad sino <c>gate_profile.hasPrendaGate</c>.
    /// Mismo cuerpo que <see cref="EvaluateMatriculaInicial"/>, con un nombre que no presupone familia.
    /// </summary>
    public static string? EvaluateDecision(
        ProcedureInstancePrenda? prendaVigente,
        IReadOnlyCollection<string> docTipos)
        => EvaluateNucleo(prendaVigente, docTipos);

    private static string? EvaluateNucleo(
        ProcedureInstancePrenda? prendaVigente,
        IReadOnlyCollection<string> docTipos)
    {
        if (prendaVigente is null)
            return TramiteEstadoErrores.PrendaDecisionRequerida;

        if (PrendaDecision.RequiereDocumento(prendaVigente.Decision))
        {
            var docTipo = PrendaDecision.DocTipoFor(prendaVigente.Decision);
            var tieneDoc = docTipo is not null
                && docTipos.Any(t => string.Equals(t, docTipo, StringComparison.OrdinalIgnoreCase));
            if (!tieneDoc)
                return TramiteEstadoErrores.PrendaDocumentoRequerido;
        }

        if (PrendaDecision.ImplicaGravamen(prendaVigente.Decision)
            && (string.IsNullOrWhiteSpace(prendaVigente.AcreedorNombre) || string.IsNullOrWhiteSpace(prendaVigente.AcreedorDocumento)))
        {
            return TramiteEstadoErrores.PrendaAcreedorRequerido;
        }

        return null;
    }

    /// <summary>
    /// CF-06 (HU #10881) — override del Organismo de Tránsito: exige el documento de prenda con
    /// INDEPENDENCIA del semáforo de gravámenes (a diferencia de <see cref="Evaluate"/>, que solo
    /// actúa en traspaso con el check <c>gravamenes</c> en warn). Así una matrícula inicial que
    /// CONSTITUYE prenda —vehículo nuevo financiado, sin gravamen previo que detectar— sigue
    /// exigiendo su soporte. Basta con que el trámite tenga adjunto CUALQUIER documento de prenda
    /// conocido (solicitud, registro o levantamiento) para satisfacer el gate.
    /// <c>otRequiereDocumentoPrenda</c> ya viene resuelto con el comportamiento SNAPSHOT (AC2): solo
    /// <c>true</c> cuando el override ya estaba activo al crear el trámite.
    ///
    /// <para><b>Independiente del semáforo, NO de la decisión (2026-08-12).</b> Nació ignorando
    /// también la decisión, y eso lo volvía insatisfacible: con <c>sin_prenda</c> u <c>omitir</c> el
    /// paso de prenda no OFRECE cargar documento —el conjunto que habilita la carga es exactamente
    /// <see cref="PrendaDecision.RequierenDocumento"/>—, así que el trámite quedaba bloqueado por un
    /// adjunto que la interfaz nunca pedía y que el gestor no tenía forma de aportar. El síntoma real:
    /// una matrícula inicial con <c>sin_prenda</c> atascada en "Finalizar". Ahora el gate comparte
    /// predicado con la UI: si la decisión no pide documento, el override calla.</para>
    ///
    /// <para><b>Sin decisión tomada (<c>null</c>) exige la decisión, no calla (2026-08-12, corrección
    /// de la misma tanda).</b> Delegar ese hueco en <see cref="Evaluate"/> solo funciona en TRASPASO
    /// con el semáforo de gravámenes en warn: en matrícula inicial <see cref="Evaluate"/> sale por
    /// <c>!esTraspaso</c> y no hay ningún otro gate de prenda, así que el gestor que nunca abre el
    /// paso de prenda radicaba sin el certificado que el OT exige — justo lo que el override existe
    /// para impedir. <c>prenda_decision_requerida</c> es accionable y satisfacible (tomar la decisión
    /// siempre está a mano), a diferencia del documento imposible que motivó el cambio de arriba.</para>
    ///
    /// <para>Y si un OT necesita el certificado sí o sí ante un gravamen, lo que corresponde es no
    /// ofrecer <c>omitir</c> en ese trámite —decidirlo al elegir—, no aceptar la elección y bloquear
    /// después con una exigencia imposible.</para>
    /// </summary>
    public static string? EvaluateOtOverride(
        bool otRequiereDocumentoPrenda,
        string? decision,
        IReadOnlyCollection<string> docTipos)
    {
        if (!otRequiereDocumentoPrenda)
            return null;

        ArgumentNullException.ThrowIfNull(docTipos);

        if (string.IsNullOrWhiteSpace(decision))
            return TramiteEstadoErrores.PrendaDecisionRequerida;

        if (!PrendaDecision.RequiereDocumento(decision))
            return null;

        var tieneDocumentoPrenda = docTipos.Any(t => PrendaDocTipos.All.Contains(t));

        return tieneDocumentoPrenda ? null : TramiteEstadoErrores.PrendaDocumentoRequeridoOt;
    }
}
