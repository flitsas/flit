using Flit.PersonaNatural.Api.Consultas;
using Flit.PersonaNatural.Api.Cotizaciones;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;

namespace Flit.PersonaNatural.Api.Solicitudes;

/// <summary>
/// Comprueba en el servidor que el vehículo del borrador está matriculado, según el RUNT, en el organismo del borrador: una placa de
/// un organismo no puede seguir en otro ni en uno no habilitado (portal web y punto). Usa la regla única
/// <see cref="CoincidenciaOrganismo"/> y deja en <c>datos.organismoVerificadoId</c> contra qué organismo se comprobó, para no volver
/// a consultar. No se compara cuando el tipo deja el organismo al operador. Un proveedor caído NO decide: sale como 503.
/// </summary>
public sealed class VerificadorDeOrganismo(VehiculoDelBorradorService vehiculo, CotizacionesService catalogo)
{
    public const string CodigoNoVerificado = "organismo_no_verificado";

    public const string MensajeSinConsultar = "Consulta de nuevo el vehículo para poder enviar el trámite.";

    /// <summary>
    /// Devuelve los datos del borrador con la verificación fijada (el llamador los guarda), o <c>null</c> si no hay nada que cambiar:
    /// el tipo deja el organismo al operador, ya estaba verificado contra este mismo organismo o, sin <paramref name="exigir"/>,
    /// no hay con qué consultar. Con <paramref name="exigir"/> (radicar) todo lo que no se pueda comprobar se rechaza.
    /// </summary>
    /// <exception cref="ReglaDeNegocioException">422 <c>organismo_no_corresponde</c> / <c>organismo_no_verificado</c>, o 503 si el RUNT no responde.</exception>
    public async Task<DatosSolicitud?> VerificarAsync(SolicitudTramite s, DefinicionTipoDto def, DocumentoConsulta? propietario, bool exigir, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(def);
        if (def.Flags.OrganismoLoEligeOperador || s.OrganismoId == Guid.Empty)
            return null;

        var datos = DatosSolicitud.Leer(s.Datos);
        if (string.Equals(datos.OrganismoVerificadoId, s.OrganismoId.ToString(), StringComparison.OrdinalIgnoreCase))
            return null;

        OrganismoDelVehiculo organismoRunt;
        try
        {
            organismoRunt = await vehiculo.OrganismoDelVehiculoAsync(s.Id, propietario, ct).ConfigureAwait(false);
        }
        catch (ProveedorNoDisponibleException)
        {
            throw new ReglaDeNegocioException(
                StatusCodes.Status503ServiceUnavailable, ConsultasEndpoints.CodigoProveedor, "Consulta no disponible",
                "No pudimos consultar el RUNT en este momento. Intente de nuevo en unos minutos.");
        }

        if (organismoRunt.Estado != EstadoVehiculoBorrador.Ok || !organismoRunt.Existe)
        {
            if (!exigir)
                return null; // el diagnóstico de FLIT 2 dirá qué falta; radicar vuelve a exigirlo
            throw new ReglaDeNegocioException(
                StatusCodes.Status422UnprocessableEntity, CodigoNoVerificado, "Organismo sin verificar",
                organismoRunt.Estado == EstadoVehiculoBorrador.Ok ? "No encontramos un vehículo con esa placa." : MensajeSinConsultar);
        }

        if (string.IsNullOrWhiteSpace(organismoRunt.OrganismoRunt))
            throw NoCorresponde(CoincidenciaOrganismo.MensajeOrganismoDesconocido);

        var candidatos = new List<string?> { s.OrganismoNombre };
        try
        {
            var propio = (await catalogo.OrganismosAsync(ct).ConfigureAwait(false))
                .FirstOrDefault(o => string.Equals(o.Id, s.OrganismoId.ToString(), StringComparison.OrdinalIgnoreCase));
            if (propio is not null)
                candidatos.AddRange([propio.Nombre, propio.Municipio, propio.Codigo]);
        }
        catch (FachadaException)
        {
            // Sin catálogo se compara solo con el nombre guardado en el borrador: no se acepta de más.
        }

        if (!CoincidenciaOrganismo.Coincide(organismoRunt.OrganismoRunt, [.. candidatos]))
            throw NoCorresponde(CoincidenciaOrganismo.MensajeOtroOrganismo(organismoRunt.OrganismoRunt, s.OrganismoNombre));

        return datos with { OrganismoVerificadoId = s.OrganismoId.ToString() };
    }

    private static ReglaDeNegocioException NoCorresponde(string mensaje) =>
        new(StatusCodes.Status422UnprocessableEntity, CoincidenciaOrganismo.CodigoNoCorresponde, "Organismo no corresponde", mensaje);
}
