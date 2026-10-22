using System.Globalization;
using System.Text.Json.Serialization;
using Flit.Consultas.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Valor de UNA fuente de avalúo: clave de la fuente (<c>fasecolda</c>, <c>mercado_libre</c>, <c>base_gravable</c>), estado
/// (<c>OK</c>, <c>SIN_DATOS</c> o <c>ERROR</c>), valor en pesos (solo con OK), número de muestras si la fuente lo informa y la
/// fecha (AAAA-MM-DD, hora Colombia) en que se obtuvo. Nunca el mensaje del proveedor.
/// </summary>
public sealed record ValorFuenteAvaluo(string Fuente, string Estado, long? Valor, int? Muestras, string? Fecha)
{
    public const string Ok = "OK";
    public const string SinDatos = "SIN_DATOS";
    public const string Error = "ERROR";
}

/// <summary>
/// Resultado normalizado del avalúo comercial (HU #13795, F05-C7), guardado en el payload de la celda AVALUO en snake_case:
/// <c>sin_valor_comercial</c> (ninguna fuente con valor: la celda queda COMPLETADO, no ERROR, decisión Q4 = b),
/// <c>valor_sugerido</c> y <c>fuente_principal</c> (nulos sin valor) y <c>fuentes</c> con el valor de cada fuente y su fecha.
/// core-consultas no informa la fecha de cada fuente: es el día en que se consultó (<see cref="ConFecha"/>).
/// </summary>
/// <example>
/// <code>
/// var normalizado = ResultadoAvaluo.Normalizar(respuesta).ConFecha(new DateOnly(2026, 10, 22));
/// // { sin_valor_comercial: false, valor_sugerido: 48500000, fuente_principal: "fasecolda", fuentes: [...] }
/// </code>
/// </example>
public sealed record ResultadoAvaluo(bool SinValorComercial, long? ValorSugerido, string? FuentePrincipal, IReadOnlyList<ValorFuenteAvaluo> Fuentes)
{
    /// <summary>
    /// Lee el valor sugerido, la fuente principal y, de cada fuente, su clave, estado, valor (solo con OK y mayor que 0) y
    /// muestras; nunca el mensaje. Sin valor sugerido (0) el resultado es <see cref="SinValorComercial"/>.
    /// </summary>
    public static ResultadoAvaluo Normalizar(ConsultarAvaluosResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var fuentes = respuesta.Avaluos
            .Where(a => !string.IsNullOrWhiteSpace(a.Fuente))
            .Select(a =>
            {
                var estado = Estado(a.Estado);
                return new ValorFuenteAvaluo(
                    a.Fuente.Trim().ToLowerInvariant(), estado, estado == ValorFuenteAvaluo.Ok && a.Valor > 0 ? a.Valor : null,
                    a.Muestras > 0 ? a.Muestras : null, null);
            })
            .ToList();
        return respuesta.ValorSugerido > 0
            ? new ResultadoAvaluo(false, respuesta.ValorSugerido, Texto(respuesta.FuentePrincipal), fuentes)
            : new ResultadoAvaluo(true, null, null, fuentes);
    }

    /// <summary>
    /// Verdadero si core-consultas respondió sin valor y con TODAS sus fuentes en ERROR: los proveedores no respondieron, así
    /// que no es «sin valor comercial» sino un error transitorio.
    /// </summary>
    [JsonIgnore]
    public bool TodasEnError => SinValorComercial && Fuentes.Count > 0 && Fuentes.All(f => f.Estado == ValorFuenteAvaluo.Error);

    /// <summary>Las fuentes sin fecha toman <paramref name="fecha"/> (el día en que se consultaron).</summary>
    public ResultadoAvaluo ConFecha(DateOnly fecha)
    {
        var texto = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return this with { Fuentes = [.. Fuentes.Select(f => f.Fecha is null ? f with { Fecha = texto } : f)] };
    }

    private static string Estado(EstadoAvaluo estado) => estado switch
    {
        EstadoAvaluo.Ok => ValorFuenteAvaluo.Ok,
        EstadoAvaluo.Error => ValorFuenteAvaluo.Error,
        _ => ValorFuenteAvaluo.SinDatos,
    };

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();
}
