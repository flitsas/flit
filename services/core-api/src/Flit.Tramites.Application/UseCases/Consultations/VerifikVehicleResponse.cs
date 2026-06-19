using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs tipados para deserializar la respuesta de Verifik RUNT vehicle-by-vin (§3.4)
/// y vehicle-by-plate (§3.3). Todos los escalares llegan como string. Los nombres de
/// propiedad mapean los campos JSON via <see cref="JsonPropertyNameAttribute"/>.
/// El typo cilidraje (by-plate) vs cilindraje (by-vin) se soporta con ambas props.
/// </summary>
public sealed class VerifikVehicleResponse
{
    [JsonPropertyName("data")]
    public VerifikVehicleData? Data { get; set; }
}

public sealed class VerifikVehicleData
{
    [JsonPropertyName("informacionGeneral")]
    public VerifikInformacionGeneral? InformacionGeneral { get; set; }

    [JsonPropertyName("soat")]
    public List<VerifikSoat>? Soat { get; set; }

    // RUNT real: "tecnoMecanica" es un ARRAY de revisiones (cada una con vigente SI/NO/NO APLICA).
    [JsonPropertyName("tecnoMecanica")]
    public List<VerifikTecnomecanica>? TecnoMecanica { get; set; }

    // RUNT real: "garantiasMobiliarias" es un ARRAY (normalmente []). La señal de gravámenes
    // vive en informacionGeneral.tieneGravamenes/prendas, NO aquí. Se mapea como lista solo
    // para no romper la deserialización (array-vs-objeto era la causa de la JsonException).
    [JsonPropertyName("garantiasMobiliarias")]
    public List<object>? GarantiasMobiliarias { get; set; }
}

public sealed class VerifikInformacionGeneral
{
    [JsonPropertyName("noPlaca")]
    public string? NoPlaca { get; set; }

    [JsonPropertyName("noVin")]
    public string? NoVin { get; set; }

    [JsonPropertyName("modelo")]
    public string? Modelo { get; set; }

    [JsonPropertyName("estadoDelVehiculo")]
    public string? EstadoDelVehiculo { get; set; }

    // Señal de gravámenes real: viven aquí (strings "SI"/"NO"), no en garantiasMobiliarias.
    [JsonPropertyName("tieneGravamenes")]
    public string? TieneGravamenes { get; set; }

    [JsonPropertyName("prendas")]
    public string? Prendas { get; set; }

    // by-vin: "cilindraje" (con n)
    [JsonPropertyName("cilindraje")]
    public string? Cilindraje { get; set; }

    // by-plate: "cilidraje" (typo Verifik, sin n)
    [JsonPropertyName("cilidraje")]
    public string? Cilidraje { get; set; }

    /// <summary>Cilindraje tolerante al typo: prioriza la forma correcta.</summary>
    [JsonIgnore]
    public string? CilindrajeNormalizado => Cilindraje ?? Cilidraje;
}

public sealed class VerifikSoat
{
    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }
}

public sealed class VerifikTecnomecanica
{
    [JsonPropertyName("vigente")]
    public string? Vigente { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }

    [JsonPropertyName("cdaExpide")]
    public string? CdaExpide { get; set; }
}
