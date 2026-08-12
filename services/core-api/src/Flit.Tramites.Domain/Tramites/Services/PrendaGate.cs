using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// R10 (HU #10597) — gate PURO de prenda del traspaso. Con el semáforo de gravámenes en <c>warn</c>,
/// el traspaso exige una decisión de prenda vigente y, si la decisión requiere documento (solicitar/
/// registrar/levantar), su adjunto. <c>omitir</c>/<c>sin_prenda</c> satisfacen el gate sin documento
/// (<c>omitir</c> = la vía "asumo el riesgo"). Devuelve el código de error o <c>null</c> si puede avanzar.
/// La detección del <c>warn</c> y la carga de la prenda son IO (viven en el servicio de ciclo de vida);
/// aquí solo la regla, para poder probarla sin cablear todo el traspaso.
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
    /// <para>Sin decisión tomada (<c>null</c>) tampoco bloquea: ese hueco lo cubre
    /// <see cref="Evaluate"/> con <c>prenda_decision_requerida</c>, que es el mensaje accionable.
    /// Y si un OT necesita el certificado sí o sí ante un gravamen, lo que corresponde es no ofrecer
    /// <c>omitir</c> en ese trámite —decidirlo al elegir—, no aceptar la elección y bloquear después
    /// con una exigencia imposible.</para>
    /// </summary>
    public static string? EvaluateOtOverride(
        bool otRequiereDocumentoPrenda,
        string? decision,
        IReadOnlyCollection<string> docTipos)
    {
        if (!otRequiereDocumentoPrenda || !PrendaDecision.RequiereDocumento(decision))
            return null;

        ArgumentNullException.ThrowIfNull(docTipos);

        var tieneDocumentoPrenda = docTipos.Any(t => PrendaDocTipos.All.Contains(t));

        return tieneDocumentoPrenda ? null : TramiteEstadoErrores.PrendaDocumentoRequeridoOt;
    }
}
