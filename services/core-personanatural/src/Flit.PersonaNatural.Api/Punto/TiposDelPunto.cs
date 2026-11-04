using Flit.PersonaNatural.Api.Consultas;

namespace Flit.PersonaNatural.Api.Punto;

/// <summary>
/// Mensajes y utilidades del canal del punto. HU 13: ya no hay lista fija de tipos; el arranque y el selector usan el catálogo
/// del organismo de la sesión (tipo elegible, habilitado y con tarifa vigente ahí), igual que el portal web.
/// </summary>
public static class TiposDelPunto
{
    public const string PlacaInexistente = "No encontramos un vehículo con esa placa.";
    public const string OtroOrganismo = "Este trámite se hace en el organismo que tiene el vehículo.";
    public const string RuntCaido = "No pudimos consultar el RUNT. Inténtalo de nuevo.";
    public const string TipoNoDisponible = "El tipo de trámite no está disponible.";
    public const string MultasCaidas = "No pudimos consultar las multas. Inténtalo de nuevo.";

    /// <summary>
    /// Mensaje cuando la placa es de otro organismo: dice dónde está registrada y dónde se hace. Usa el nombre del RUNT tal cual
    /// (solo recorta y colapsa espacios) y el nombre del catálogo de la sesión. No lleva datos del propietario. Sin nombre del RUNT
    /// (no ocurre al rechazar) cae al texto genérico.
    /// </summary>
    public static string MensajeOtroOrganismo(string? organismoRunt, OrganismoPunto sesion)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        return CoincidenciaOrganismo.MensajeOtroOrganismo(organismoRunt, sesion.Nombre);
    }

    /// <summary>El RUNT no trajo el organismo del vehículo: no se sigue (igual que el portal web).</summary>
    public const string OrganismoDesconocido = CoincidenciaOrganismo.MensajeOrganismoDesconocido;

    /// <summary>Campo que el mapper del RUNT de FLIT 2 escribe (<c>KyverumRuntVehicleResultMapper</c>). El portal lo compara contra el catálogo de grants; el punto lo compara con la sesión.</summary>
    public const string CampoOrganismoRunt = "transit_office_name";


    /// <summary>
    /// El nombre que devolvió el RUNT es el de la sesión (nombre, código o, si se conoce, municipio) según la regla única
    /// <see cref="CoincidenciaOrganismo"/>, la misma del portal web. Sin nombre del RUNT NO coincide: quien llama decide
    /// (el portal tampoco deja seguir sin organismo, salvo los tipos que elige el operador).
    /// </summary>
    public static bool OrganismoDeLaSesion(string? nombreRunt, OrganismoPunto sesion, string? municipio = null)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        return CoincidenciaOrganismo.Coincide(nombreRunt, sesion.Nombre, sesion.Codigo, municipio);
    }

    /// <summary>
    /// Igual, pero si por nombre o código no coincide busca el municipio del organismo de la sesión en el catálogo (la cookie del punto
    /// no lo trae). El catálogo solo se pide cuando hace falta; si no responde se queda con el resultado por nombre.
    /// </summary>
    public static async Task<bool> OrganismoDeLaSesionAsync(string? nombreRunt, OrganismoPunto sesion, Cotizaciones.IFachadaTramites? catalogo, CancellationToken ct)
    {
        if (OrganismoDeLaSesion(nombreRunt, sesion))
            return true;
        if (catalogo is null || string.IsNullOrWhiteSpace(nombreRunt))
            return false;

        try
        {
            var organismos = await catalogo.ListarOrganismosAsync(ct).ConfigureAwait(false);
            var propio = organismos.FirstOrDefault(o => string.Equals(o.Id, sesion.OrganismoId.ToString(), StringComparison.OrdinalIgnoreCase));
            return propio is not null && CoincidenciaOrganismo.Coincide(nombreRunt, propio.Nombre, propio.Municipio);
        }
        catch (Cotizaciones.FachadaException)
        {
            return false;
        }
    }
}

/// <summary>Organismo fijo de la cookie <c>pn_punto</c>. La persona no lo cambia: el valor va protegido.</summary>
public sealed record OrganismoPunto(Guid OrganismoId, string Codigo, string Nombre);
