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

    /// <summary>
    /// Bloque de identidad resuelta contra la Registraduría. Fuente PREFERENTE del nombre: es el
    /// único que llega siempre sin enmascarar (ver <see cref="KyverumRuntPersona.Nombres"/>).
    /// </summary>
    [JsonPropertyName("identidad")]
    public KyverumRuntIdentidad? Identidad { get; set; }

    [JsonPropertyName("persona")]
    public KyverumRuntPersona? Persona { get; set; }

    [JsonPropertyName("licencias")]
    public List<KyverumRuntLicencia>? Licencias { get; set; }

    [JsonPropertyName("multas")]
    public KyverumRuntMultas? Multas { get; set; }
}

/// <summary>
/// Identidad resuelta (bloque <c>identidad</c>). Trae el nombre real desglosado; es lo que el
/// mapper prefiere sobre cualquier otro campo.
/// </summary>
public sealed class KyverumRuntIdentidad
{
    [JsonPropertyName("primerNombre")]
    public string? PrimerNombre { get; set; }

    [JsonPropertyName("segundoNombre")]
    public string? SegundoNombre { get; set; }

    [JsonPropertyName("primerApellido")]
    public string? PrimerApellido { get; set; }

    [JsonPropertyName("segundoApellido")]
    public string? SegundoApellido { get; set; }

    [JsonPropertyName("nombreCompleto")]
    public string? NombreCompleto { get; set; }
}

public sealed class KyverumRuntPersona
{
    /// <summary>
    /// ENMASCARADO por el RUNT desde su actualización de 2026 ("S****L"). NO usarlo para hidratar
    /// nada: existe solo para no perder el campo del contrato. El nombre real está en el bloque
    /// <c>identidad</c> o en los campos desglosados de abajo.
    /// </summary>
    [JsonPropertyName("nombres")]
    public string? Nombres { get; set; }

    /// <summary>ENMASCARADO — ver <see cref="Nombres"/>.</summary>
    [JsonPropertyName("apellidos")]
    public string? Apellidos { get; set; }

    [JsonPropertyName("primerNombre")]
    public string? PrimerNombre { get; set; }

    [JsonPropertyName("segundoNombre")]
    public string? SegundoNombre { get; set; }

    [JsonPropertyName("primerApellido")]
    public string? PrimerApellido { get; set; }

    [JsonPropertyName("segundoApellido")]
    public string? SegundoApellido { get; set; }

    [JsonPropertyName("nombreCompleto")]
    public string? NombreCompleto { get; set; }

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
