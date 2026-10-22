namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Clase de error de una fuente (patrón <c>TipoErrorFuente</c> de Comparendos). <see cref="Timeout"/> y <see cref="Red"/> son
/// transitorios (se reintentan con backoff, D-12); los demás son definitivos.
/// </summary>
public enum TipoErrorFuente
{
    /// <summary>El proveedor no respondió a tiempo (transitorio).</summary>
    Timeout,

    /// <summary>Falla de red hacia el proveedor (transitorio).</summary>
    Red,

    /// <summary>La respuesta no tiene la forma esperada o el proveedor la marcó como error (definitivo).</summary>
    RespuestaInvalida,

    /// <summary>La fuente no está conectada en este ambiente: no se llamó a ningún proveedor (definitivo).</summary>
    NoConectado,
}

/// <summary>Tipo de propiedad del propietario en el RUNT (<c>detallePropiedad</c> PROPIO|LEASING o <c>tipoPropiedad</c> 1|3).</summary>
public enum TipoPropiedad
{
    Propio,
    Leasing,

    /// <summary>El proveedor no informó un valor conocido.</summary>
    Desconocido,
}

/// <summary>Locatario de un vehículo en leasing: solo tipo (CC, CE, NIT, PAS o TI) y número del documento, sin nombre.</summary>
public sealed record LocatarioRunt(string TipoDocumento, string NumeroDocumento);

/// <summary>Propietario actual: tipo y número del documento, tipo de propiedad y sus locatarios; nunca el nombre.</summary>
public sealed record PropietarioRunt(string TipoDocumento, string NumeroDocumento, TipoPropiedad TipoPropiedad, IReadOnlyList<LocatarioRunt> Locatarios);

/// <summary>
/// El vehículo tal como lo reporta el RUNT, normalizado por el producto. <see cref="Locatarios"/> es la unión, sin repetir,
/// de <c>lessee[]</c> y de los <c>locatarios[]</c> de cada propietario. Los datos técnicos del bien (HU #13795: marca, línea,
/// modelo, cilindraje, combustible y pasajeros) son opcionales: los usa el avalúo comercial y van a
/// <c>datos_consolidados.vehiculo</c>; nulos si el RUNT no los trae.
/// </summary>
public sealed record VehiculoRunt(
    string? Placa,
    string? Vin,
    string? Servicio,
    string? Clase,
    string? Organismo,
    IReadOnlyList<PropietarioRunt> Propietarios,
    IReadOnlyList<LocatarioRunt> Locatarios,
    string ServicioQueResolvio,
    string? IdTransaccion)
{
    public string? Marca { get; init; }

    public string? Linea { get; init; }

    /// <summary>Año modelo.</summary>
    public int? Modelo { get; init; }

    /// <summary>Cilindraje en c.c.</summary>
    public int? Cilindraje { get; init; }

    public string? Combustible { get; init; }

    /// <summary>Capacidad de pasajeros.</summary>
    public int? Pasajeros { get; init; }
}

/// <summary>Resultado de <see cref="IFuenteRunt.ConsultarVehiculoAsync"/>: el vehículo o un error tipado, nunca los dos.</summary>
public sealed record ResultadoRunt
{
    private ResultadoRunt(VehiculoRunt? vehiculo, TipoErrorFuente? error, string? mensaje)
    {
        Vehiculo = vehiculo;
        Error = error;
        Mensaje = mensaje;
    }

    public VehiculoRunt? Vehiculo { get; }

    public TipoErrorFuente? Error { get; }

    /// <summary>Mensaje técnico del error, sin documentos ni datos del vehículo.</summary>
    public string? Mensaje { get; }

    public bool Exitoso => Vehiculo is not null;

    /// <summary>Verdadero si el error se puede reintentar (<see cref="TipoErrorFuente.Timeout"/> o <see cref="TipoErrorFuente.Red"/>).</summary>
    public bool EsTransitorio => Error is TipoErrorFuente.Timeout or TipoErrorFuente.Red;

    public static ResultadoRunt Exito(VehiculoRunt vehiculo)
    {
        ArgumentNullException.ThrowIfNull(vehiculo);
        return new(vehiculo, null, null);
    }

    public static ResultadoRunt Fallo(TipoErrorFuente error, string mensaje) => new(null, error, mensaje);
}
