namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Fuente RUNT simulada de Intempo (HU #13725, RD-1): para probar el motor en DEV sin proveedor real. Solo se registra con
/// <c>Diagnostico:Simulacion:Habilitada=true</c> y <c>Diagnostico:Ambiente</c> LOCAL o DEV (ver
/// <see cref="Modulos.AddFuentesConsultas"/>). Responde con los fixtures inventados de <see cref="FixturesIntempo"/> (proto-JSON
/// de <c>ConsultarVehiculoIntempoResponse</c>, HU #13877, G5=a), pasados por <see cref="MapperIntempo.MapearJson"/>, la misma
/// regla de la fuente real; los errores van dentro del mensaje. Es determinista por el último carácter del identificador (plan
/// §9.1); el vehículo devuelto lleva la placa o el VIN consultado:
/// <list type="table">
/// <item><term>1</term><description>propietario persona natural (CC)</description></item>
/// <item><term>2</term><description>propietario NIT</description></item>
/// <item><term>3</term><description>leasing con locatario C (cédula)</description></item>
/// <item><term>4</term><description>leasing con locatario N (NIT)</description></item>
/// <item><term>5</term><description>servicio público</description></item>
/// <item><term>6</term><description>organismo de un departamento no soportado</description></item>
/// <item><term>7</term><description>error transitorio <see cref="TipoErrorFuente.Timeout"/></description></item>
/// <item><term>8</term><description>error definitivo <see cref="TipoErrorFuente.RespuestaInvalida"/></description></item>
/// <item><term>otro</term><description>persona natural</description></item>
/// </list>
/// No llama a ningún proveedor ni registra documentos en logs.
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarVehiculoAsync(empresa, new IdentificadorVehiculo(TipoIdentificador.PlacaCarro, "ABC123"), ct);
/// // "ABC123" termina en 3 → leasing con locatario C
/// </code>
/// </example>
public sealed class FuenteRuntIntempoSimulada : IFuenteRunt
{
    public const string Servicio = "INTEMPO_SIMULADO";

    private const string PorDefecto = "intempo-persona-natural.json";

    private static readonly Dictionary<char, string> Escenarios = new()
    {
        ['1'] = PorDefecto,
        ['2'] = "intempo-nit.json",
        ['3'] = "intempo-leasing-locatario-c.json",
        ['4'] = "intempo-leasing-locatario-n.json",
        ['5'] = "intempo-servicio-publico.json",
        ['6'] = "intempo-departamento-no-soportado.json",
        ['7'] = "intempo-error-timeout.json",
        ['8'] = "intempo-error-respuesta-invalida.json",
    };

    public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identificador);
        ct.ThrowIfCancellationRequested();
        var valor = identificador.Valor.Trim();
        var archivo = valor.Length > 0 && Escenarios.TryGetValue(valor[^1], out var a) ? a : PorDefecto;
        var resultado = MapperIntempo.MapearJson(FixturesIntempo.Todos()[archivo], Servicio);
        if (resultado.Vehiculo is { } vehiculo)
        {
            resultado = ResultadoRunt.Exito(identificador.EsVin
                ? vehiculo with { Vin = valor }
                : vehiculo with { Placa = valor });
        }

        return Task.FromResult(resultado);
    }
}

/// <summary>
/// Respuestas inventadas del RUNT por Intempo versionadas como recursos del ensamblado (<c>Fixtures/intempo-*.json</c>): proto-JSON
/// de <c>ConsultarVehiculoIntempoResponse</c> con el vehículo en las claves de §5 (HU #13877), datos ficticios (rango declarado
/// y auditado por <c>FixturesSinPiiTests</c>). Nunca se construyen a partir de una muestra real.
/// </summary>
public static class FixturesIntempo
{
    private static readonly string Prefijo = $"{typeof(FixturesIntempo).Assembly.GetName().Name}.Fixtures.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Cargados = new(Cargar);

    /// <summary>Nombre del archivo → contenido JSON.</summary>
    public static IReadOnlyDictionary<string, string> Todos() => Cargados.Value;

    private static Dictionary<string, string> Cargar()
    {
        var ensamblado = typeof(FixturesIntempo).Assembly;
        var fixtures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var recurso in ensamblado.GetManifestResourceNames().Where(n => n.StartsWith(Prefijo + "intempo-", StringComparison.Ordinal)))
        {
            using var stream = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(stream);
            fixtures[recurso[Prefijo.Length..]] = lector.ReadToEnd();
        }

        return fixtures;
    }
}
