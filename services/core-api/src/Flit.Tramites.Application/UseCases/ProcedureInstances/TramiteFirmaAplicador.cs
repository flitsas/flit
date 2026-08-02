using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// "Firmar un trámite" según su modalidad: traspaso → firma de la compraventa; matrícula → FUR. Es la
/// misma política que ya aplica el encadenado automático de la HU #10349; se nombra aquí para que el
/// lote de firma a posteriori (HU #11196) no la reimplemente y para poder sustituirla en pruebas —los
/// dos handlers concretos son <c>sealed</c> y no admiten dobles.
/// </summary>
public interface ITramiteFirmaAplicador
{
    /// <summary>
    /// Firma la parte indicada del trámite. Devuelve qué acción se ejecutó (para la traza) y el error del
    /// handler si el trámite todavía no es apto (p. ej. le falta un gate).
    /// </summary>
    Task<(string Accion, string? Error)> AplicarAsync(
        ProcedureInstance instance, Guid tenantId, string parte, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación real: delega en los handlers existentes, que además regeneran los documentos, así que
/// el sello de la firma sale sin invalidar nada a mano.
/// </summary>
public sealed class TramiteFirmaAplicador(
    SolicitarFirmaHandler firmaHandler,
    GenerarFurHandler furHandler) : ITramiteFirmaAplicador
{
    public async Task<(string Accion, string? Error)> AplicarAsync(
        ProcedureInstance instance, Guid tenantId, string parte, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // El mandatario no es parte de la compraventa: lo único que hay que rehacer es la generación
        // documental, que es donde se estampa su firma en el contrato de mandato. Pasarlo por la firma de
        // la compraventa le atribuiría un rol que no tiene en el negocio jurídico.
        if (string.Equals(parte, MarcarFirmaPosteriorHandler.ParteMandatario, StringComparison.Ordinal))
        {
            var (_, mandatoError) = await furHandler
                .HandleAsync(instance.Id, tenantId, cancellationToken)
                .ConfigureAwait(false);
            return ("mandato", mandatoError);
        }

        var esTraspaso = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada)
            == TramiteModalidadEntrada.Traspaso;

        if (esTraspaso)
        {
            var (_, error) = await firmaHandler
                .HandleAsync(instance.Id, tenantId, new SolicitarFirmaInput(parte, null), cancellationToken)
                .ConfigureAwait(false);
            return ("firma_compraventa", error);
        }

        var (_, furError) = await furHandler
            .HandleAsync(instance.Id, tenantId, cancellationToken)
            .ConfigureAwait(false);
        return ("fur", furError);
    }
}
