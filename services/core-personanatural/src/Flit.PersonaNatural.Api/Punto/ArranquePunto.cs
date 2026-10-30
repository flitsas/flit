using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;

namespace Flit.PersonaNatural.Api.Punto;

public abstract record ResultadoArranque
{
    public sealed record Seguir : ResultadoArranque;

    public sealed record Detener(string Mensaje) : ResultadoArranque;

    public sealed record ProveedorCaido : ResultadoArranque;

    public sealed record DatosInvalidos(string Mensaje) : ResultadoArranque;
}

/// <summary>
/// Consulta de arranque del punto (HU #14219). Reutiliza la consulta RUNT de vehículo del portal y compara el organismo
/// del RUNT (<c>transit_office_name</c>) con el de la cookie, no con el catálogo de organismos elegibles del portal.
/// No persiste <c>procedure instance</c> ni solicitud de radicación.
/// </summary>
public sealed class ArranquePuntoService(IConsultasRunt runt, IFachadaTramites catalogo)
{
    public async Task<ResultadoArranque> EvaluarAsync(OrganismoPunto sesion, string? placa, string? documento, string? tipoCodigo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoArranque.DatosInvalidos("Indique el tipo de trámite.");

        TipoTramiteDto? tipo = null;
        try
        {
            var tipos = await catalogo.ListarTiposAsync(null, ct).ConfigureAwait(false);
            tipo = tipos.FirstOrDefault(t => string.Equals(t.Code, codigo, StringComparison.OrdinalIgnoreCase));
        }
        catch (FachadaException ex) when (TiposDelPunto.Admite(codigo, null))
        {
            _ = ex;
        }
        catch (FachadaException ex)
        {
            return new ResultadoArranque.Detener(string.IsNullOrWhiteSpace(ex.Message) ? TiposDelPunto.TipoNoDisponible : ex.Message);
        }

        if (!TiposDelPunto.Admite(codigo, tipo?.Family))
        {
            var motivo = tipo is { MotivoNoDisponible: { Length: > 0 } m } ? m : TiposDelPunto.TipoNoDisponible;
            return new ResultadoArranque.Detener(motivo);
        }

        var placaNorm = EntradaConsulta.Placa(placa);
        var propietario = EntradaConsulta.DocumentoPropietario("CC", documento);
        if (placaNorm is null || propietario is null)
            return new ResultadoArranque.DatosInvalidos("Indique la placa y el documento del propietario.");

        ResultadoConsulta resultado;
        try
        {
            resultado = await runt.ConsultarVehiculoAsync(placaNorm, null, propietario, "punto", ct).ConfigureAwait(false);
        }
        catch (ProveedorNoDisponibleException)
        {
            return new ResultadoArranque.ProveedorCaido();
        }

        if (ProyeccionMinima.ProveedorCaido(resultado))
            return new ResultadoArranque.ProveedorCaido();
        if (!ProyeccionMinima.Vehiculo(resultado, placaNorm).Existe)
            return new ResultadoArranque.Detener(TiposDelPunto.PlacaInexistente);

        var organismoRunt = ProyeccionMinima.Texto(resultado, TiposDelPunto.CampoOrganismoRunt);
        if (!TiposDelPunto.OrganismoDeLaSesion(organismoRunt, sesion))
            return new ResultadoArranque.Detener(TiposDelPunto.OtroOrganismo);

        return new ResultadoArranque.Seguir();
    }
}
