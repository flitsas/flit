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

    /// <summary>
    /// Apellidos ya separados. Verifik los incluye solo en ALGUNAS respuestas (llegan para unos
    /// documentos y para otros no), así que son una pista preferente, no una garantía: sin ellos el
    /// mapper separa <see cref="LastName"/> por su cuenta.
    /// </summary>
    [JsonPropertyName("primerApellido")]
    public string? PrimerApellido { get; set; }

    /// <summary>Ver <see cref="PrimerApellido"/>.</summary>
    [JsonPropertyName("segundoApellido")]
    public string? SegundoApellido { get; set; }

    [JsonPropertyName("citizenStatus")]
    public string? CitizenStatus { get; set; }

    [JsonPropertyName("driverStatus")]
    public string? DriverStatus { get; set; }

    [JsonPropertyName("totalLicenses")]
    public string? TotalLicenses { get; set; }

    [JsonPropertyName("licenses")]
    public List<VerifikConductorLicense>? Licenses { get; set; }

    [JsonPropertyName("infractions")]
    public VerifikConductorInfractions? Infractions { get; set; }

    [JsonPropertyName("identityValidationAttempts")]
    public VerifikConductorIdentityValidation? IdentityValidationAttempts { get; set; }
}

public sealed class VerifikConductorLicense
{
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("expeditionDate")]
    public string? ExpeditionDate { get; set; }

    [JsonPropertyName("licenceNumber")]
    public string? LicenceNumber { get; set; }

    [JsonPropertyName("otExpide")]
    public string? OtExpide { get; set; }

    [JsonPropertyName("restrictions")]
    public string? Restrictions { get; set; }
}

public sealed class VerifikConductorInfractions
{
    [JsonPropertyName("nroPazYSalvo")]
    public string? NroPazYSalvo { get; set; }

    [JsonPropertyName("tieneMultas")]
    public string? TieneMultas { get; set; }
}

public sealed class VerifikConductorIdentityValidation
{
    [JsonPropertyName("estadoUsuario")]
    public string? EstadoUsuario { get; set; }
}
