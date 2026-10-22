using Flit.Consultas.Grpc.V1;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Un comparendo normalizado: solo número, fecha (AAAA-MM-DD), valor (texto decimal en pesos), estado y organismo.</summary>
public sealed record ComparendoSimit(string? Numero, string? Fecha, string? Valor, string? Estado, string? Organismo);

/// <summary>
/// Resultado normalizado de SIMIT (HU #13793, F05-C9): el semáforo que devolvió core-consultas (<c>VERDE</c>, <c>AMARILLO</c>,
/// <c>ROJO</c>; null si no lo informa), la cantidad de comparendos y cada comparendo con <see cref="ComparendoSimit"/>. Nunca
/// datos del infractor (nombres, documento), la infracción, los datos del chequeo ni la respuesta cruda. El total no se calcula
/// en el producto (frente C1). Se guarda en el payload de la celda en snake_case.
/// </summary>
/// <example>
/// <code>
/// var normalizado = ResultadoSimit.Normalizar(respuesta.Resultado); // { semaforo, cantidad_comparendos, comparendos[] }
/// </code>
/// </example>
public sealed record ResultadoSimit(string? Semaforo, int CantidadComparendos, IReadOnlyList<ComparendoSimit> Comparendos)
{
    /// <summary>Lee SOLO el semáforo y los comparendos de los chequeos; los repetidos por número se cuentan una vez.</summary>
    public static ResultadoSimit Normalizar(ResultadoConsulta resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var comparendos = new List<ComparendoSimit>();
        var numeros = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in resultado.Chequeos.SelectMany(ch => ch.Comparendos))
        {
            var numero = Texto(c.HasNumero ? c.Numero : null);
            if (numero is not null && !numeros.Add(numero))
                continue;
            comparendos.Add(new ComparendoSimit(
                numero, Texto(c.HasFecha ? c.Fecha : null), Texto(c.HasValor ? c.Valor : null), Texto(c.HasEstado ? c.Estado : null),
                Texto(c.HasOrganismo ? c.Organismo : null)));
        }

        return new ResultadoSimit(CodigoSemaforo(resultado.Semaforo), comparendos.Count, comparendos);
    }

    private static string? CodigoSemaforo(Flit.Consultas.Grpc.V1.Semaforo semaforo) => semaforo switch
    {
        Flit.Consultas.Grpc.V1.Semaforo.Verde => "VERDE",
        Flit.Consultas.Grpc.V1.Semaforo.Amarillo => "AMARILLO",
        Flit.Consultas.Grpc.V1.Semaforo.Rojo => "ROJO",
        _ => null,
    };

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
