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

    /// <summary>Campo que el mapper del RUNT de FLIT 2 escribe (<c>KyverumRuntVehicleResultMapper</c>). El portal lo compara contra el catálogo de grants; el punto lo compara con la sesión.</summary>
    public const string CampoOrganismoRunt = "transit_office_name";


    /// <summary>
    /// El nombre que devolvió el RUNT es el de la sesión (o su código público). Sin nombre no hay contra qué comparar:
    /// igual que el preflight de FLIT 2, no se niega el trámite por un dato que no vino. No se consulta el catálogo de
    /// organismos elegibles del portal.
    /// </summary>
    public static bool OrganismoDeLaSesion(string? nombreRunt, OrganismoPunto sesion)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        if (string.IsNullOrWhiteSpace(nombreRunt))
            return true;

        var nombre = nombreRunt.Trim();
        return string.Equals(nombre, sesion.Nombre.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(nombre, sesion.Codigo.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Organismo fijo de la cookie <c>pn_punto</c>. La persona no lo cambia: el valor va protegido.</summary>
public sealed record OrganismoPunto(Guid OrganismoId, string Codigo, string Nombre);
