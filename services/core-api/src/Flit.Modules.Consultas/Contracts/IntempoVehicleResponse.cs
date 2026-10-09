using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs para la respuesta INTEMPO vehículo (§4.1 VIN / §4.2 PLACA).
/// Mismo endpoint, mismo schema: sólo cambia el body de la petición.
/// Todos los escalares vienen como string ("SI"/"NO", cifras como "0", "1761").
/// El campo noPlaca es genérico: contiene VIN cuando tipoConsulta=VIN.
/// El resultado exitoso trae codigoResultado="Exitoso"; error de negocio también
/// viene como HTTP 200 con codigoResultado="Error".
/// </summary>
public sealed class IntempoVehicleResponse
{
    [JsonPropertyName("noRegistro")]
    public string? NoRegistro { get; set; }

    [JsonPropertyName("noLicenciaTransito")]
    public string? NoLicenciaTransito { get; set; }

    [JsonPropertyName("estadoDelVehiculo")]
    public string? EstadoDelVehiculo { get; set; }

    [JsonPropertyName("noPlaca")]
    public string? NoPlaca { get; set; }

    [JsonPropertyName("noVin")]
    public string? NoVin { get; set; }

    [JsonPropertyName("noChasis")]
    public string? NoChasis { get; set; }

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("linea")]
    public string? Linea { get; set; }

    [JsonPropertyName("modelo")]
    public string? Modelo { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("cilindraje")]
    public string? Cilindraje { get; set; }

    [JsonPropertyName("tieneGravamenes")]
    public string? TieneGravamenes { get; set; }

    [JsonPropertyName("prendas")]
    public string? Prendas { get; set; }

    [JsonPropertyName("prendario")]
    public string? Prendario { get; set; }

    [JsonPropertyName("nombreAcreedor")]
    public string? NombreAcreedor { get; set; }

    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }

    [JsonPropertyName("claseVehiculo")]
    public string? ClaseVehiculo { get; set; }

    [JsonPropertyName("tipoCarroceria")]
    public string? TipoCarroceria { get; set; }

    [JsonPropertyName("tipoCombustible")]
    public string? TipoCombustible { get; set; }

    [JsonPropertyName("fechaMatricula")]
    public string? FechaMatricula { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

    [JsonPropertyName("esRegrabadoMotor")]
    public string? EsRegrabadoMotor { get; set; }

    [JsonPropertyName("esRegrabadoChasis")]
    public string? EsRegrabadoChasis { get; set; }

    [JsonPropertyName("esRegrabadoVin")]
    public string? EsRegrabadoVin { get; set; }

    [JsonPropertyName("repotenciado")]
    public string? Repotenciado { get; set; }

    [JsonPropertyName("soatNacionales")]
    public List<IntempoSoat>? SoatNacionales { get; set; }

    [JsonPropertyName("gravamenes")]
    public List<IntempoGravamen>? Gravamenes { get; set; }

    [JsonPropertyName("limitacionesPropiedad")]
    public List<object>? LimitacionesPropiedad { get; set; }

    [JsonPropertyName("propietariosActuales")]
    public List<IntempoPropietario>? PropietariosActuales { get; set; }

    [JsonPropertyName("codigoResultado")]
    public string? CodigoResultado { get; set; }

    [JsonPropertyName("codigoUUID")]
    public string? CodigoUUID { get; set; }

    [JsonPropertyName("fecha")]
    public string? Fecha { get; set; }
}

public sealed class IntempoSoat
{
    [JsonPropertyName("noPoliza")]
    public string? NoPoliza { get; set; }

    [JsonPropertyName("fechaExpedicion")]
    public string? FechaExpedicion { get; set; }

    [JsonPropertyName("fechaVigencia")]
    public string? FechaVigencia { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }

    [JsonPropertyName("nitEntidad")]
    public string? NitEntidad { get; set; }

    [JsonPropertyName("entidadExpideSoat")]
    public string? EntidadExpideSoat { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }
}

public sealed class IntempoGravamen
{
    [JsonPropertyName("idPrenda")]
    public long? IdPrenda { get; set; }

    [JsonPropertyName("tipoDocumentoAcreedor")]
    public string? TipoDocumentoAcreedor { get; set; }

    [JsonPropertyName("numeroDocumentoAcreedor")]
    public string? NumeroDocumentoAcreedor { get; set; }

    [JsonPropertyName("nombreAcreedor")]
    public string? NombreAcreedor { get; set; }

    [JsonPropertyName("fechaInscripcion")]
    public string? FechaInscripcion { get; set; }

    [JsonPropertyName("estadoPrenda")]
    public string? EstadoPrenda { get; set; }
}

public sealed class IntempoPropietario
{
    [JsonPropertyName("noDocumento")]
    public string? NoDocumento { get; set; }

    [JsonPropertyName("nombreCompleto")]
    public string? NombreCompleto { get; set; }

    [JsonPropertyName("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    [JsonPropertyName("detallePropiedad")]
    public string? DetallePropiedad { get; set; }
}
