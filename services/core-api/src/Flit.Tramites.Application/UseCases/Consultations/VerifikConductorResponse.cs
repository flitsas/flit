using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs para la respuesta CONDUCTOR de Verifik (RUNT). Único endpoint de persona de
/// Verifik que devuelve nombre. Relevante para autopoblar el comprador:
/// data.fullName / data.firstName / data.lastName y el estado de licencia.
/// </summary>
public sealed class VerifikConductorResponse
{
    [JsonPropertyName("data")]
    public VerifikConductorData? Data { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class VerifikConductorData
{
    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("documentNumber")]
    public string? DocumentNumber { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("identityValidationAttempts")]
    public VerifikConductorIdentityValidation? IdentityValidationAttempts { get; set; }
}

public sealed class VerifikConductorIdentityValidation
{
    [JsonPropertyName("estadoUsuario")]
    public string? EstadoUsuario { get; set; }
}
