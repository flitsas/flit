using System.Text.Json.Serialization;

namespace Flit.Infrastructure.Consultations.Avaluos;

/// <summary>Respuesta de <c>busquedaVin</c>: <c>{ message, codigos: [] }</c>.</summary>
internal sealed class FasecoldaVinResponse
{
    public string? Message { get; set; }
    public List<string>? Codigos { get; set; }
}

/// <summary>Respuesta OAuth2 password de <c>/token</c> (snake_case).</summary>
internal sealed class FasecoldaTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

/// <summary>Ficha técnica de <c>consultabycodigo</c> (solo los campos usados en el filtro/selección).</summary>
internal sealed class FasecoldaCodeItem
{
    public string? Codigo { get; set; }
    public int? Cilindraje { get; set; }
    public string? Combustible { get; set; }
    public int? Puertas { get; set; }
    public int? CapacidadPasajeros { get; set; }
    public List<FasecoldaValorModelo>? ValorModelo { get; set; }
}

internal sealed class FasecoldaValorModelo
{
    public string? Modelo { get; set; }
    public decimal Valor { get; set; }
}
