using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs tipados para deserializar la respuesta de Kyverum RUNT <c>POST /v1/vehiculos:consultar</c>
/// (VIN o placa+documento) — ver <c>context/reference/kyverum-runt/CONTRATO-API.md</c>. Kyverum es la
/// fuente canónica de estas consultas (HU #10478): el mapper converge al mismo
/// <see cref="ConsultationResult"/> que Verifik. Solo se modelan los campos que consume
/// <see cref="KyverumRuntVehicleResultMapper"/>; el resto de la superficie del RUNT se ignora
/// (System.Text.Json descarta propiedades no mapeadas). <c>ok:false</c> se maneja en el cliente HTTP,
/// no aquí.
/// </summary>
public sealed class KyverumRuntVehicleResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public KyverumRuntVehicleData? Data { get; set; }
}

public sealed class KyverumRuntVehicleData
{
    [JsonPropertyName("vehiculo")]
    public KyverumRuntVehiculo? Vehiculo { get; set; }

    // Tipo de documento del propietario que RESUELVE el propio RUNT (C=CC, N=NIT, E=CE, T=TI, P=PAS…).
    // Kyverum lo devuelve aunque no se envíe en la consulta por placa; lo usamos para sembrar el tipo
    // del vendedor en traspaso (HU #10478) sin pedírselo al usuario.
    [JsonPropertyName("tipoDocPropietario")]
    public string? TipoDocPropietario { get; set; }

    [JsonPropertyName("datosTecnicos")]
    public KyverumRuntDatosTecnicos? DatosTecnicos { get; set; }

    [JsonPropertyName("soat")]
    public List<KyverumRuntSoat>? Soat { get; set; }

    [JsonPropertyName("rtm")]
    public List<KyverumRuntRtm>? Rtm { get; set; }
}

public sealed class KyverumRuntVehiculo
{
    [JsonPropertyName("placa")]
    public string? Placa { get; set; }

    [JsonPropertyName("vin")]
    public string? Vin { get; set; }

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("linea")]
    public string? Linea { get; set; }

    [JsonPropertyName("modelo")]
    public string? Modelo { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("clase")]
    public string? Clase { get; set; }

    [JsonPropertyName("tipoCombustible")]
    public string? TipoCombustible { get; set; }

    [JsonPropertyName("numChasis")]
    public string? NumChasis { get; set; }

    [JsonPropertyName("numMotor")]
    public string? NumMotor { get; set; }

    [JsonPropertyName("numSerie")]
    public string? NumSerie { get; set; }

    [JsonPropertyName("cilindraje")]
    public string? Cilindraje { get; set; }

    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }

    [JsonPropertyName("tipoCarroceria")]
    public string? TipoCarroceria { get; set; }

    [JsonPropertyName("pasajerosSentados")]
    public string? PasajerosSentados { get; set; }

    [JsonPropertyName("pesoBruto")]
    public string? PesoBruto { get; set; }

    [JsonPropertyName("numeroEjes")]
    public string? NumeroEjes { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

    [JsonPropertyName("estadoAutomotor")]
    public string? EstadoAutomotor { get; set; }

    // Señal de gravámenes/prendas: strings "SI"/"NO" en el propio vehículo (igual que el RUNT vía Verifik).
    [JsonPropertyName("gravamenes")]
    public string? Gravamenes { get; set; }

    [JsonPropertyName("prendas")]
    public string? Prendas { get; set; }
}

public sealed class KyverumRuntDatosTecnicos
{
    // Fallback de peso/ejes cuando el bloque vehiculo no los trae.
    [JsonPropertyName("pesoBrutoVehicular")]
    public string? PesoBrutoVehicular { get; set; }

    [JsonPropertyName("noEjes")]
    public string? NoEjes { get; set; }
}

public sealed class KyverumRuntSoat
{
    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("fechaVencimSoat")]
    public string? FechaVencimSoat { get; set; }

    [JsonPropertyName("razonSocialAsegur")]
    public string? RazonSocialAsegur { get; set; }
}

public sealed class KyverumRuntRtm
{
    // Vigencia de la revisión técnico-mecánica: "SI" / "NO" / "NO APLICA" (mismo dominio que Verifik).
    // OJO: la RTM de Kyverum NO usa "estado"/"fechaVencimiento" (como sí el SOAT), sino "vigente" y
    // "fechaVencimientoRvt". Leer los nombres equivocados dejaba la RTM en null → novedad falsa
    // "RTM no vigente" aunque el RUNT sí traía una revisión vigente.
    [JsonPropertyName("vigente")]
    public string? Vigente { get; set; }

    // Estado del trámite de la RVT ("APROBADA", ...). Informativo.
    [JsonPropertyName("estadoRvt")]
    public string? EstadoRvt { get; set; }

    [JsonPropertyName("fechaVencimientoRvt")]
    public string? FechaVencimientoRvt { get; set; }
}
