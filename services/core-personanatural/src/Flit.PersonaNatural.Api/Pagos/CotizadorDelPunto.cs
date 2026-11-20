using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Solicitudes;

namespace Flit.PersonaNatural.Api.Pagos;

/// <summary>
/// Cotiza el pago de una solicitud del punto con el medio PIN y el ámbito del organismo (Features #14392/#14395), siempre en
/// vivo: el total se recalcula hasta que se confirma el pago. Traduce los motivos del catálogo a los códigos del paso Pago.
/// </summary>
public sealed class CotizadorDelPunto(IFachadaMediosPago medios)
{
    /// <summary>Medio de la fachada de precios que corresponde al pago en caja.</summary>
    public const string MedioPin = "PIN";

    public const string CodigoSinTarifa = "sin_tarifa";
    public const string CodigoMedioNoDisponible = "medio_no_disponible";

    public async Task<CotizacionDto> CotizarAsync(string tipoCodigo, Guid organismoId, CancellationToken ct)
    {
        try
        {
            return await medios.CotizarConMedioAsync(tipoCodigo, organismoId.ToString(), [], MedioPin, organismoId.ToString(), ct).ConfigureAwait(false);
        }
        catch (FachadaException ex) when (ex.Codigo == "tarifa_no_vigente")
        {
            throw new ReglaDeNegocioException(StatusCodes.Status409Conflict, CodigoSinTarifa, "Sin tarifa", "Este trámite aún no tiene tarifa en el organismo elegido.");
        }
        catch (FachadaException ex) when (ex.Codigo == CodigoMedioNoDisponible)
        {
            throw new ReglaDeNegocioException(StatusCodes.Status409Conflict, CodigoMedioNoDisponible, "Pago en caja no disponible", "El punto no tiene habilitado el pago en caja.");
        }
    }

    /// <summary>Líneas y total como JSON, la misma forma que se congela al radicar.</summary>
    public static string SerializarLineas(IEnumerable<LineaCobroDto> lineas) =>
        System.Text.Json.JsonSerializer.Serialize(lineas.Select(l => new { codigo = l.Codigo, nombre = l.Nombre, subtitulo = l.Subtitulo, valorCop = l.ValorCop }));
}
