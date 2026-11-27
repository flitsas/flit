using Flit.Diagnostico.Domain.Consultas;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Configuración global de las consultas (<c>Diagnostico:Consultas</c>, HU #13737, D-12; ajuste 2026-11-11 Punto 3): igual para
/// todas las compañías, no por compañía. Fuera de rango el servicio no arranca.
/// <list type="bullet">
/// <item><see cref="MaxReintentos"/>: reintentos automáticos de errores transitorios por celda, en
/// [<see cref="PoliticaReintentos.Minimo"/>, <see cref="PoliticaReintentos.Maximo"/>] (por defecto <see cref="PoliticaReintentos.PorDefecto"/> = 3).</item>
/// <item><see cref="EsperaReintento"/>: espera BASE del backoff (HU #14752: 1, 2 y 4 veces la base), en
/// [<see cref="PoliticaReintentos.EsperaMinima"/>, <see cref="PoliticaReintentos.EsperaMaxima"/>] (por defecto 1 min). Ya no es el TTL de
/// ninguna cola: las de espera tienen un tick fijo (<c>ProgramadorReintentos.TickCola</c>).</item>
/// <item><see cref="FactorAleatorio"/>: desvío aleatorio de cada espera, en [0, <see cref="PoliticaReintentos.FactorAleatorioMaximo"/>]
/// (por defecto 0,2 = ±20 %).</item>
/// <item><see cref="EsperaMinimaLimiteProveedor"/>: espera mínima de un <c>LIMITE_PROVEEDOR</c>, en [1 s, 10 min] (por defecto 1 min).</item>
/// <item><see cref="UmbralPegada"/>: tiempo sin cambio a partir del cual una celda PENDIENTE/EN_CURSO cuenta como pegada
/// (<see cref="CriterioPegada"/>, por defecto 10 min); mayor que la espera máxima efectiva de la política
/// (<see cref="PoliticaReintentos.EsperaMaximaEfectiva"/>: una celda que espera su reintento no está pegada) y como máximo 1 día.</item>
/// </list>
/// </summary>
public sealed class OpcionesConsultas
{
    public const string Seccion = "Diagnostico:Consultas";

    /// <summary>Umbral máximo de «pegada».</summary>
    public static readonly TimeSpan UmbralMaximo = TimeSpan.FromDays(1);

    /// <summary>
    /// Máximo de colas de espera (<c>ProgramadorReintentos.EsperasDe</c>) que puede dar la política: 64 ticks de 15 s, es decir una
    /// espera máxima efectiva de 15 min como mucho (HU #14752).
    /// </summary>
    public const int MaximoColasEspera = 64;

    /// <summary>Mensaje de arranque con un valor fuera de rango (indica las reglas de las cinco claves y el tope de colas de espera).</summary>
    public static readonly string MensajeFueraDeRango =
        $"{Seccion}:{nameof(MaxReintentos)} debe estar en el rango [{PoliticaReintentos.Minimo}, {PoliticaReintentos.Maximo}] (D-12); "
        + $"{Seccion}:{nameof(EsperaReintento)} entre {PoliticaReintentos.EsperaMinima} y {PoliticaReintentos.EsperaMaxima}; "
        + $"{Seccion}:{nameof(FactorAleatorio)} entre 0 y {PoliticaReintentos.FactorAleatorioMaximo.ToString(System.Globalization.CultureInfo.InvariantCulture)}; "
        + $"{Seccion}:{nameof(EsperaMinimaLimiteProveedor)} entre {PoliticaReintentos.EsperaMinima} y {PoliticaReintentos.EsperaMinimaLimiteMaxima}; "
        + $"{Seccion}:{nameof(UmbralPegada)} mayor que la espera máxima efectiva del backoff y como máximo {UmbralMaximo}; "
        + $"{Seccion}:{nameof(EsperaReintento)} y {Seccion}:{nameof(MaxReintentos)} no pueden dar más de {MaximoColasEspera} colas de espera "
        + $"de {Bus.ProgramadorReintentos.TickCola.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} s (espera máxima efectiva del backoff).";

    public int MaxReintentos { get; set; } = PoliticaReintentos.PorDefecto;

    public TimeSpan EsperaReintento { get; set; } = PoliticaReintentos.EsperaPorDefecto;

    public double FactorAleatorio { get; set; } = PoliticaReintentos.FactorAleatorioPorDefecto;

    public TimeSpan EsperaMinimaLimiteProveedor { get; set; } = PoliticaReintentos.EsperaMinimaLimitePorDefecto;

    public TimeSpan UmbralPegada { get; set; } = CriterioPegada.UmbralPorDefecto;

    /// <summary>Las reglas que no se cumplen; vacía si la configuración es válida.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (MaxReintentos is < PoliticaReintentos.Minimo or > PoliticaReintentos.Maximo)
            errores.Add($"{nameof(MaxReintentos)} fuera de [{PoliticaReintentos.Minimo}, {PoliticaReintentos.Maximo}]");
        var esperaValida = EsperaValida;
        if (!esperaValida)
            errores.Add($"{nameof(EsperaReintento)} fuera de [{PoliticaReintentos.EsperaMinima}, {PoliticaReintentos.EsperaMaxima}]");
        var factorValido = FactorValido;
        if (!factorValido)
            errores.Add($"{nameof(FactorAleatorio)} fuera de [0, {PoliticaReintentos.FactorAleatorioMaximo.ToString(System.Globalization.CultureInfo.InvariantCulture)}]");
        var minimoValido = MinimoValido;
        if (!minimoValido)
            errores.Add($"{nameof(EsperaMinimaLimiteProveedor)} fuera de [{PoliticaReintentos.EsperaMinima}, {PoliticaReintentos.EsperaMinimaLimiteMaxima}]");
        // El umbral se compara con la espera más larga que puede dar el backoff; con claves inválidas, con la de por defecto.
        var politica = Politica(esperaValida, factorValido, minimoValido);
        var maxima = politica.EsperaMaximaEfectiva;
        if (UmbralPegada <= maxima || UmbralPegada > UmbralMaximo)
            errores.Add($"{nameof(UmbralPegada)} debe ser mayor que la espera máxima efectiva ({maxima}) y como máximo {UmbralMaximo}");
        // HU #14752: cada tick de 15 s de la espera más larga es una cola; un backoff muy largo declararía cientos de colas.
        var colas = Bus.ProgramadorReintentos.EsperasDe(politica).Count;
        if (colas > MaximoColasEspera)
            errores.Add($"{nameof(EsperaReintento)} y {nameof(MaxReintentos)} dan {colas} colas de espera (espera máxima efectiva {maxima}); el máximo es {MaximoColasEspera}");
        return errores;
    }

    /// <summary>La política con estas claves; las inválidas toman su valor por defecto.</summary>
    public PoliticaReintentos Politica() => Politica(EsperaValida, FactorValido, MinimoValido);

    private bool EsperaValida => EsperaReintento >= PoliticaReintentos.EsperaMinima && EsperaReintento <= PoliticaReintentos.EsperaMaxima;

    private bool FactorValido => !double.IsNaN(FactorAleatorio) && FactorAleatorio >= 0 && FactorAleatorio <= PoliticaReintentos.FactorAleatorioMaximo;

    private bool MinimoValido =>
        EsperaMinimaLimiteProveedor >= PoliticaReintentos.EsperaMinima && EsperaMinimaLimiteProveedor <= PoliticaReintentos.EsperaMinimaLimiteMaxima;

    private PoliticaReintentos Politica(bool esperaValida, bool factorValido, bool minimoValido) => new(
        MaxReintentos is >= PoliticaReintentos.Minimo and <= PoliticaReintentos.Maximo ? MaxReintentos : PoliticaReintentos.PorDefecto,
        esperaValida ? EsperaReintento : PoliticaReintentos.EsperaPorDefecto,
        factorValido ? FactorAleatorio : PoliticaReintentos.FactorAleatorioPorDefecto,
        minimoValido ? EsperaMinimaLimiteProveedor : PoliticaReintentos.EsperaMinimaLimitePorDefecto);

    /// <summary>Verdadero si las cinco claves cumplen sus reglas.</summary>
    public bool EsValida() => Errores().Count == 0;
}
