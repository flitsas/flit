using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Configuración global de las consultas (<c>Diagnostico:Consultas</c>, HU #13737, D-12; ajuste 2026-11-11 Punto 3): igual para
/// todas las compañías, no por compañía. Fuera de rango el servicio no arranca.
/// <list type="bullet">
/// <item><see cref="MaxReintentos"/>: reintentos automáticos de errores transitorios por celda, en
/// [<see cref="PoliticaReintentos.Minimo"/>, <see cref="PoliticaReintentos.Maximo"/>] (por defecto <see cref="PoliticaReintentos.PorDefecto"/> = 3).</item>
/// <item><see cref="EsperaReintento"/>: espera fija entre intentos, en [<see cref="PoliticaReintentos.EsperaMinima"/>,
/// <see cref="PoliticaReintentos.EsperaMaxima"/>] (por defecto 2 min). Es también el TTL de las colas
/// <c>diagnostico.consultas-resultados.retry.n</c>.</item>
/// <item><see cref="UmbralPegada"/>: tiempo sin cambio a partir del cual una celda PENDIENTE/EN_CURSO cuenta como pegada
/// (<see cref="CriterioPegada"/>, por defecto 10 min); mayor que <see cref="EsperaReintento"/> (una celda que espera su reintento no
/// está pegada) y como máximo 1 día.</item>
/// </list>
/// </summary>
public sealed class OpcionesConsultas
{
    public const string Seccion = "Diagnostico:Consultas";

    /// <summary>Umbral máximo de «pegada».</summary>
    public static readonly TimeSpan UmbralMaximo = TimeSpan.FromDays(1);

    /// <summary>Mensaje de arranque con un valor fuera de rango (indica las reglas de las tres claves).</summary>
    public static readonly string MensajeFueraDeRango =
        $"{Seccion}:{nameof(MaxReintentos)} debe estar en el rango [{PoliticaReintentos.Minimo}, {PoliticaReintentos.Maximo}] (D-12); "
        + $"{Seccion}:{nameof(EsperaReintento)} entre {PoliticaReintentos.EsperaMinima} y {PoliticaReintentos.EsperaMaxima}; "
        + $"{Seccion}:{nameof(UmbralPegada)} mayor que {nameof(EsperaReintento)} y como máximo {UmbralMaximo}.";

    public int MaxReintentos { get; set; } = PoliticaReintentos.PorDefecto;

    public TimeSpan EsperaReintento { get; set; } = PoliticaReintentos.EsperaPorDefecto;

    public TimeSpan UmbralPegada { get; set; } = CriterioPegada.UmbralPorDefecto;

    /// <summary>Las reglas que no se cumplen; vacía si la configuración es válida.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (MaxReintentos is < PoliticaReintentos.Minimo or > PoliticaReintentos.Maximo)
            errores.Add($"{nameof(MaxReintentos)} fuera de [{PoliticaReintentos.Minimo}, {PoliticaReintentos.Maximo}]");
        if (EsperaReintento < PoliticaReintentos.EsperaMinima || EsperaReintento > PoliticaReintentos.EsperaMaxima)
            errores.Add($"{nameof(EsperaReintento)} fuera de [{PoliticaReintentos.EsperaMinima}, {PoliticaReintentos.EsperaMaxima}]");
        if (UmbralPegada <= EsperaReintento || UmbralPegada > UmbralMaximo)
            errores.Add($"{nameof(UmbralPegada)} debe ser mayor que {nameof(EsperaReintento)} y como máximo {UmbralMaximo}");
        return errores;
    }

    /// <summary>Verdadero si las tres claves cumplen sus reglas.</summary>
    public bool EsValida() => Errores().Count == 0;
}
