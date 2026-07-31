using System.Text.Json;
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

    [JsonPropertyName("marca")]
    public string? Marca { get; set; }

    [JsonPropertyName("linea")]
    public string? Linea { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("claseVehiculo")]
    public string? ClaseVehiculo { get; set; }

    [JsonPropertyName("tipoCombustible")]
    public string? TipoCombustible { get; set; }

    [JsonPropertyName("organismoTransito")]
    public string? OrganismoTransito { get; set; }

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

    [JsonPropertyName("tipoServicio")]
    public string? TipoServicio { get; set; }

    [JsonPropertyName("tipoCarroceria")]
    public string? TipoCarroceria { get; set; }

    [JsonPropertyName("noChasis")]
    public string? NoChasis { get; set; }

    [JsonPropertyName("noMotor")]
    public string? NoMotor { get; set; }

    [JsonPropertyName("noSerie")]
    public string? NoSerie { get; set; }

    [JsonPropertyName("pasajerosSentados")]
    public string? PasajerosSentados { get; set; }

    [JsonPropertyName("pesoBruto")]
    public string? PesoBruto { get; set; }

    [JsonPropertyName("noEjes")]
    public string? NoEjes { get; set; }

    [JsonPropertyName("fechaMatricula")]
    public string? FechaMatricula { get; set; }
}

public sealed class VerifikSoat
{
    [JsonPropertyName("estado")]
    public string? Estado { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }

    [JsonPropertyName("entidadExpideSoat")]
    public string? EntidadExpideSoat { get; set; }

    // HU #11134 — el registro de SOAT del RUNT trae la póliza y sus fechas; el modelo solo declaraba
    // tres campos y descartaba el resto al deserializar, así que media tabla del certificado dependía
    // del OCR del PDF que cargara el operador. Los mismos nombres que ya declara el modelo de Intempo,
    // que representa el mismo registro del RUNT.
    [JsonPropertyName("noPoliza")]
    public string? NoPoliza { get; set; }

    [JsonPropertyName("fechaExpedicion")]
    public string? FechaExpedicion { get; set; }

    [JsonPropertyName("fechaVigencia")]
    public string? FechaVigencia { get; set; }
}

/// <summary>
/// Registro de revisión técnico-mecánica del RUNT.
///
/// <para><b>HU #11135 — número de certificado y fechas de expedición/vigencia.</b> El certificado
/// del expediente los pinta, pero <b>ningún contrato disponible confirma cómo se llaman</b>: las
/// muestras reales capturadas traen la lista vacía (<c>tecnoMecanica: []</c> en Verifik, <c>rtm: []</c>
/// en Kyverum) y el modelo de Intempo no tiene bloque de RTM. Inventar nombres sería repetir el fallo
/// que originó este Feature: quedarían en null y el hueco volvería a esconderse tras un modelo que
/// aparenta cubrirlo.</para>
///
/// <para>En vez de adivinar, se conserva <b>todo</b> lo que mande el proveedor en
/// <see cref="CamposNoModelados"/> y se resuelven esos tres valores probando los nombres candidatos
/// documentados en <c>VerifikResultMapper</c>. Si el proveedor usa cualquiera de ellos, el dato entra
/// hoy; si usa otro, queda capturado y visible en vez de perdido, y el OCR del PDF sigue de respaldo.</para>
/// </summary>
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

    /// <summary>Todo lo que el proveedor envía y el modelo no declara. Sin esto se descartaba en silencio.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? CamposNoModelados { get; set; }
}
