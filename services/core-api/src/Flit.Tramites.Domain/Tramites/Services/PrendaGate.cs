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
}
