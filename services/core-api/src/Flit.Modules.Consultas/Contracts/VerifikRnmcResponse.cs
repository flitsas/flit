using System.Text.Json.Serialization;

namespace Flit.Tramites.Application.UseCases.Consultations;

/// <summary>
/// DTOs para la respuesta RNMC de Verifik (§3.1).
/// Semáforo relevante: data.correctiveMeasures[] — vacío = sin medidas.
/// </summary>
public sealed class VerifikRnmcResponse
{
    [JsonPropertyName("data")]
    public VerifikRnmcData? Data { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class VerifikRnmcData
{
    [JsonPropertyName("correctiveMeasures")]
    public List<VerifikRnmcCorrectiveMeasure>? CorrectiveMeasures { get; set; }

    [JsonPropertyName("records")]
    public List<VerifikRnmcRecord>? Records { get; set; }

    [JsonPropertyName("arrayName")]
    public List<string>? ArrayName { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("documentNumber")]
    public string? DocumentNumber { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }
}

public sealed class VerifikRnmcCorrectiveMeasure
{
    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("measure")]
    public string? Measure { get; set; }

    [JsonPropertyName("referredTo")]
    public string? ReferredTo { get; set; }
}

public sealed class VerifikRnmcRecord
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("municipality")]
    public string? Municipality { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("identification")]
    public string? Identification { get; set; }

    [JsonPropertyName("offender")]
    public string? Offender { get; set; }
}
