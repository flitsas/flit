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

    [JsonPropertyName("revisionTecnomecanica")]
    public VerifikTecnomecanica? RevisionTecnomecanica { get; set; }

    [JsonPropertyName("garantiasMobiliarias")]
    public VerifikGravamenes? GarantiasMobiliarias { get; set; }
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

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }
}

public sealed class VerifikGravamenes
{
    [JsonPropertyName("tieneGravamenes")]
    public string? TieneGravamenes { get; set; }

    [JsonPropertyName("prendas")]
    public string? Prendas { get; set; }

    [JsonPropertyName("limitacionPropiedad")]
    public string? LimitacionPropiedad { get; set; }
}
