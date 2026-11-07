namespace Flit.PersonaNatural.Api.Persistence;

/// <summary>
/// HU #14395 (Feature #14392, Épica #14297) — lo que se cotizó al radicar, congelado en la solicitud: el medio, las líneas con
/// sus valores, el total y el dato de versión de tarifa que devolvió core-api. Es la única fuente de lo que se cobra a ese
/// trámite: un cambio posterior de precio o de switch no la toca, y nada la actualiza después de insertarla. Una por solicitud.
/// Sin datos personales.
/// </summary>
public sealed class CotizacionCongelada
{
    public Guid SolicitudId { get; set; }

    /// <summary>PASARELA o PIN.</summary>
    public string Medio { get; set; } = string.Empty;

    /// <summary>Portal cuya configuración rigió: el organismo del punto, o nulo si la solicitud vino por la web.</summary>
    public Guid? AmbitoOrganismoId { get; set; }

    /// <summary>JSON de las líneas cotizadas (<c>[{codigo, nombre, subtitulo, valorCop}]</c>).</summary>
    public string Lineas { get; set; } = "[]";

    public long TotalCop { get; set; }

    /// <summary>Ids de las versiones de tarifa usadas, tal como los devolvió core-api.</summary>
    public string VersionTarifa { get; set; } = string.Empty;

    public DateTimeOffset CongeladaEn { get; set; }
}
