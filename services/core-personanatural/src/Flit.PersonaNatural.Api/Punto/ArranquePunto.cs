using Flit.Consultas.Grpc.V1;
using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;

namespace Flit.PersonaNatural.Api.Punto;

public abstract record ResultadoArranque
{
    public sealed record Seguir : ResultadoArranque;

    public sealed record Detener(string Mensaje) : ResultadoArranque;

    public sealed record ProveedorCaido : ResultadoArranque;

    public sealed record DatosInvalidos(string Mensaje) : ResultadoArranque;
}

/// <summary>
/// Consulta de arranque del punto (HU #14219, HU 13). Reutiliza la consulta RUNT de vehículo del portal y compara el organismo
/// del RUNT (<c>transit_office_name</c>) con el de la cookie, no con el catálogo de organismos elegibles del portal. El tipo se
/// admite si el catálogo lo ofrece en el organismo de la sesión (elegible, habilitado y con tarifa vigente); ya no hay lista fija.
/// Esa comparación de organismo solo aplica cuando el RUNT lo impone: si lo elige el operador, el de la sesión ES el elegido.
/// No persiste <c>procedure instance</c> ni solicitud de radicación.
/// </summary>
public sealed class ArranquePuntoService(IConsultasRunt runt, IFachadaTramites catalogo, DefinicionesService? definiciones = null)
{
    public async Task<ResultadoArranque> EvaluarAsync(OrganismoPunto sesion, string? placa, string? documento, string? tipoCodigo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var codigo = (tipoCodigo ?? string.Empty).Trim();
        if (codigo.Length == 0)
            return new ResultadoArranque.DatosInvalidos("Indique el tipo de trámite.");

        TipoTramiteDto? tipo;
        try
        {
            var tipos = await catalogo.ListarTiposAsync(sesion.OrganismoId.ToString(), ct).ConfigureAwait(false);
            tipo = tipos.FirstOrDefault(t => string.Equals(t.Code, codigo, StringComparison.OrdinalIgnoreCase));
        }
        catch (FachadaException ex)
        {
            return new ResultadoArranque.Detener(string.IsNullOrWhiteSpace(ex.Message) ? TiposDelPunto.TipoNoDisponible : ex.Message);
        }

        if (tipo is not { Disponible: true })
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
        if (!await OrganismoLoEligeElOperadorAsync(codigo, sesion, ct).ConfigureAwait(false))
        {
            // Sin organismo en el RUNT tampoco se sigue (como el portal web): la persona no elige otro.
            if (string.IsNullOrWhiteSpace(organismoRunt))
                return new ResultadoArranque.Detener(TiposDelPunto.OrganismoDesconocido);
            if (!await TiposDelPunto.OrganismoDeLaSesionAsync(organismoRunt, sesion, catalogo, ct).ConfigureAwait(false))
                return new ResultadoArranque.Detener(TiposDelPunto.MensajeOtroOrganismo(organismoRunt, sesion));
        }

        return new ResultadoArranque.Seguir();
    }

    /// <summary>
    /// ¿El organismo lo elige el operador para este tipo? Se lee del flag de la definición. Sin definición legible se aplica la
    /// comparación con el RUNT (el comportamiento previo): no se relaja una regla por falta de dato.
    /// </summary>
    private async Task<bool> OrganismoLoEligeElOperadorAsync(string codigo, OrganismoPunto sesion, CancellationToken ct)
    {
        if (definiciones is null)
            return false;

        try
        {
            return (await definiciones.ObtenerAsync(codigo, sesion.OrganismoId.ToString(), ct).ConfigureAwait(false)).Flags.OrganismoLoEligeOperador;
        }
        catch (FachadaException)
        {
            return false;
        }
    }
}
