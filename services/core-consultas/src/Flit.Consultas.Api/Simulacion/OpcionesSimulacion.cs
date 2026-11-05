namespace Flit.Consultas.Api.Simulacion;

/// <summary>
/// Simulación de proveedores para LOCAL y DEV (ADR-0075, U3 = a; antes vivía en core-diagnostico como
/// <c>Diagnostico:Simulacion</c>, HU #13725/#13791). Con <see cref="ClaveHabilitada"/>=true, los trabajos del diagnóstico que llegan
/// por el bus los responde <see cref="SimuladorConsultas"/> (documentos y vehículos inventados) en vez del proveedor real; el gRPC
/// en línea no cambia. Solo se admite con <see cref="ClaveAmbiente"/> LOCAL o DEV: en otro ambiente, o sin definirlo, el servicio
/// NO arranca (la misma regla de <c>SeleccionAdaptadores</c> de diagnóstico). No mira <c>IHostEnvironment</c>: los tres ambientes
/// corren como Development.
/// </summary>
/// <example>
/// <code>
/// var simulacion = OpcionesSimulacion.Desde(builder.Configuration); // lanza si Habilitada=true en QA o PDN
/// </code>
/// </example>
internal sealed class OpcionesSimulacion
{
    /// <summary>Habilita la simulación (por defecto false).</summary>
    public const string ClaveHabilitada = "Consultas:Simulacion:Habilitada";

    /// <summary>Ambiente lógico del despliegue: LOCAL, DEV, QA o PDN (sin valor por defecto).</summary>
    public const string ClaveAmbiente = "Consultas:Ambiente";

    private static readonly string[] AmbientesConSimulacion = ["LOCAL", "DEV"];

    public bool Habilitada { get; init; }

    public string? Ambiente { get; init; }

    /// <summary>
    /// Fuentes del bus (<see cref="Bus.FuentesBus"/>) que se simulan aunque <see cref="ClaveHabilitada"/> esté apagada, separadas por
    /// coma (p. ej. «avaluo» cuando el proveedor real falla en local). Misma regla de ambiente que la simulación total.
    /// </summary>
    public const string ClaveFuentes = "Consultas:Simulacion:Fuentes";

    /// <summary>Fuentes simuladas una a una (vacío si no hay).</summary>
    public IReadOnlySet<string> Fuentes { get; init; } = new HashSet<string>();

    /// <summary>Si el trabajo de <paramref name="fuente"/> lo responde el simulador: todas con la simulación total, o las de <see cref="Fuentes"/>.</summary>
    public bool Simula(string fuente) => Habilitada || Fuentes.Contains(fuente);

    /// <summary>Lee y valida la configuración; lanza <see cref="InvalidOperationException"/> si el servicio no debe arrancar.</summary>
    public static OpcionesSimulacion Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ambiente = configuration[ClaveAmbiente]?.Trim();
        var habilitada = configuration.GetValue(ClaveHabilitada, false);
        var fuentes = (configuration[ClaveFuentes] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (!habilitada && fuentes.Count == 0)
            return new OpcionesSimulacion { Habilitada = false, Ambiente = ambiente };

        if (!AmbientesConSimulacion.Contains(ambiente, StringComparer.OrdinalIgnoreCase))
        {
            var clave = Variable(habilitada ? ClaveHabilitada : ClaveFuentes);
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {clave} solo se admite en LOCAL o DEV " +
                $"({Variable(ClaveAmbiente)}=DEV o LOCAL; valor actual: «{(string.IsNullOrEmpty(ambiente) ? "sin definir" : ambiente)}»). " +
                $"En QA y PDN quite {clave}: la simulación responde con documentos inventados.");
        }

        return new OpcionesSimulacion { Habilitada = habilitada, Fuentes = fuentes, Ambiente = ambiente!.ToUpperInvariant() };
    }

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}
