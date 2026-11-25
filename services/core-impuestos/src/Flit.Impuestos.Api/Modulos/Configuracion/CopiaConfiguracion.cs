using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Persistence.Siembra;

namespace Flit.Impuestos.Api.Modulos.Configuracion;

/// <summary>
/// Copia de la configuración vigente que queda en la cabecera del lote al crearlo (F3-CF3, D-CONFIG), en el jsonb
/// <c>liquidaciones.config_copia</c> con exactamente <c>{maxRegistros, departamentosHabilitados, concurrencia}</c>:
/// <list type="bullet">
///   <item><c>maxRegistros</c>: el operativo si hay, si no el contractual; nunca más que el contractual.</item>
///   <item><c>departamentosHabilitados</c>: los del contractual; null = todos los activos de <c>/fuentes</c>.</item>
///   <item><c>concurrencia</c>: placas en vuelo por compañía que fija quien crea el lote (opciones de ejecución, DA-5).</item>
/// </list>
/// Sin configuración guardada usa <see cref="ConfiguracionPorDefecto"/> (F5a-CF4). Es inmutable: un cambio posterior de la
/// configuración no cambia los lotes ya creados.
/// </summary>
/// <example>
/// <code>
/// var copia = await servicioConfiguracion.CopiaAsync(empresa, opciones.ConcurrenciaPorEmpresa, ct);
/// liquidacion.ConfigCopia = copia.ComoJson();
/// var habilitado = CopiaConfiguracion.DesdeJson(liquidacion.ConfigCopia).DepartamentoHabilitado("25");
/// </code>
/// </example>
public sealed record CopiaConfiguracion(
    [property: JsonPropertyName("maxRegistros")] int MaxRegistros,
    [property: JsonPropertyName("departamentosHabilitados")] IReadOnlyList<string>? DepartamentosHabilitados,
    [property: JsonPropertyName("concurrencia")] int Concurrencia)
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>La copia de <paramref name="configuracion"/> (o de los valores por defecto si es null) con la concurrencia dada.</summary>
    public static CopiaConfiguracion De(ConfiguracionEmpresa? configuracion, int concurrencia)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrencia, 1);
        var contractual = configuracion?.MaxRegistrosLoteContractual ?? ConfiguracionPorDefecto.MaxRegistrosLoteContractual;
        var maximo = Math.Min(configuracion?.MaxRegistrosLoteOperativo ?? contractual, contractual);
        string[]? departamentos = configuracion?.DepartamentosHabilitados is { } lista ? [.. lista] : null;
        return new CopiaConfiguracion(maximo, departamentos, concurrencia);
    }

    /// <summary>Lee el jsonb de la cabecera del lote.</summary>
    public static CopiaConfiguracion DesdeJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<CopiaConfiguracion>(json, Json)
            ?? throw new JsonException("La copia de configuración del lote está vacía.");
    }

    /// <summary>El jsonb <c>{maxRegistros, departamentosHabilitados, concurrencia}</c> (los null se escriben).</summary>
    public string ComoJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Verdadero si el departamento está habilitado en la copia (null = todos).</summary>
    public bool DepartamentoHabilitado(string departamento) =>
        DepartamentosHabilitados is null || DepartamentosHabilitados.Contains(departamento, StringComparer.Ordinal);
}
