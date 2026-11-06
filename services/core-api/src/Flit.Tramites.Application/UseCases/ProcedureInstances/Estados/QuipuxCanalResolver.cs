using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Quipux;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Único sitio donde se decide si un OT con un tipo dado está en el canal Quipux: tipo parametrizado
/// (<c>external_refs</c>) + OT destino integrado (DIVIPO + bandera de la familia). Lo comparten el
/// enrutamiento de <c>/submit</c> y la excepción de firma de la matrícula sin placa
/// (<see cref="FirmaGate.EximidaPorPreasignacionAsync"/>).
/// </summary>
internal static class QuipuxCanalResolver
{
    /// <summary><c>false</c> si el OT no tiene canal configurado (canal gestor/OT).</summary>
    public static async Task<bool> EsActivoAsync(
        ITransitOfficeQuipuxCanalReader reader,
        string? typeExternalRefs,
        Guid transitOfficeId,
        CancellationToken ct)
    {
        var canal = await reader.GetAsync(transitOfficeId, ct).ConfigureAwait(false);
        if (canal is null)
            return false;

        return ProcedureTypeQuipuxGate.CanalActivoParaSecretaria(
            typeExternalRefs,
            canal.DivipoCode,
            canal.QuipuxRegistration,
            canal.QuipuxTransfer,
            canal.QuipuxOther);
    }
}
