using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs tipados para deserializar la respuesta de Kyverum RUNT <c>POST /v1/personas:consultar</c>
/// (documento + tipoDocumento opcional) — ver <c>context/reference/kyverum-runt/CONTRATO-API.md</c>.
/// Fuente canónica de la consulta de conductor (HU #10478): el mapper converge al mismo
/// <see cref="ConsultationResult"/> que <c>verifik_conductor</c>. Solo se modelan los campos que
/// consume <see cref="KyverumRuntConductorResultMapper"/>; el resto se ignora. <c>ok:false</c>
/// (persona no encontrada) se maneja en el cliente HTTP, no aquí.
/// </summary>
public sealed class KyverumRuntPersonaResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("persona")]
    public KyverumRuntPersona? Persona { get; set; }

    [JsonPropertyName("licencias")]
    public List<KyverumRuntLicencia>? Licencias { get; set; }

    [JsonPropertyName("multas")]
    public KyverumRuntMultas? Multas { get; set; }
}

public sealed class KyverumRuntPersona
{
    [JsonPropertyName("nombres")]
    public string? Nombres { get; set; }

    [JsonPropertyName("apellidos")]
    public string? Apellidos { get; set; }

    [JsonPropertyName("tipoDocumento")]
    public string? TipoDocumento { get; set; }

    [JsonPropertyName("documento")]
    public string? Documento { get; set; }

    [JsonPropertyName("estadoPersona")]
    public string? EstadoPersona { get; set; }

    [JsonPropertyName("estadoConductor")]
    public string? EstadoConductor { get; set; }

    [JsonPropertyName("tieneLicencias")]
    public bool? TieneLicencias { get; set; }
}

public sealed class KyverumRuntLicencia
{
    [JsonPropertyName("numeroLicencia")]
    public string? NumeroLicencia { get; set; }

    [JsonPropertyName("estadoLicencia")]
    public string? EstadoLicencia { get; set; }

    [JsonPropertyName("detalleLicencia")]
    public List<KyverumRuntDetalleLicencia>? DetalleLicencia { get; set; }
}

public sealed class KyverumRuntDetalleLicencia
{
    [JsonPropertyName("categoria")]
    public string? Categoria { get; set; }

    [JsonPropertyName("fechaVencimiento")]
    public string? FechaVencimiento { get; set; }
}

public sealed class KyverumRuntMultas
{
    [JsonPropertyName("tieneMultas")]
    public string? TieneMultas { get; set; }

    [JsonPropertyName("nroPazYSalvo")]
    public string? NroPazYSalvo { get; set; }
}
