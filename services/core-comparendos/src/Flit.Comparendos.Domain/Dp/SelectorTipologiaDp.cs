using System.Globalization;
using Flit.Comparendos.Domain.Plazos;

namespace Flit.Comparendos.Domain.Dp;

/// <summary>Lo que el selector necesita saber de un comparendo. La API lo arma; el dominio no conoce la base.</summary>
/// <param name="CodigoInfraccion">C29, C14, D02… (null si la fuente no lo trae).</param>
/// <param name="FechaImposicion">Cuándo ocurrió la infracción.</param>
/// <param name="FechaNotificacion">Cuándo se notificó al propietario (null si no consta).</param>
/// <param name="EsFotomulta">Detectado por cámara; null si no se sabe, que se trata como cámara igual que <c>Plazos.Tipo</c>.</param>
/// <param name="Activo">El comparendo sigue vigente en las fuentes.</param>
/// <param name="Pagado">Está marcado como pagado.</param>
/// <param name="TieneDp">Ya tiene derecho de petición (solo se admite uno).</param>
/// <param name="PropietarioEsPersonaJuridica">El propietario es una persona jurídica (documento NIT).</param>
/// <param name="DescuentoVigente">Descuento por pronto pago que corre hoy, según <see cref="MotorDePlazos.Evaluar"/>.</param>
public sealed record DatosDeSeleccion(
    string? CodigoInfraccion,
    DateOnly? FechaImposicion,
    DateOnly? FechaNotificacion,
    bool? EsFotomulta,
    bool Activo,
    bool Pagado,
    bool TieneDp,
    bool PropietarioEsPersonaJuridica,
    Descuento DescuentoVigente);

/// <summary>Por qué un comparendo no recibe derecho de petición automático hoy.</summary>
public enum MotivoNoElegible
{
    Inactivo = 1,
    Pagado = 2,
    YaTieneDp = 3,

    /// <summary>De cámara con el 50 % de descuento vigente: se puede trasladar al conductor, así que el DP espera.</summary>
    VentanaDescuento = 4,

    /// <summary>Ninguna de las reglas aplica.</summary>
    SinTipologiaAplicable = 5,

    /// <summary>Alguna regla aplica pero ninguna de esas tipologías está disponible (habilitada, permitida o automatizable).</summary>
    SinTipologiaDisponible = 6,
}

/// <summary>Una tipología a la que aplica un comparendo, con la razón en lenguaje de la gestora.</summary>
public sealed record TipologiaAplicable(string Codigo, string Razon, bool Disponible);

/// <summary>
/// Resultado de decidir. <see cref="Aplicables"/> trae todas las que aplican por regla, en orden de prioridad, aunque el
/// comparendo no sea elegible: la pantalla puede mostrar qué se podría alegar y por qué hoy no se envía.
/// </summary>
public sealed record DecisionDp(string? Elegida, IReadOnlyList<TipologiaAplicable> Aplicables, MotivoNoElegible? Motivo)
{
    public bool EsElegible => Elegida is not null;
}

/// <summary>
/// Decide qué tipología de derecho de petición aplica a un comparendo (Epic #14606, HU #14608). Función pura: misma
/// entrada, misma salida; no consulta nada. Trae las 5 reglas por calendario de V1 con sus umbrales en
/// <see cref="ReglasDpAutomatico"/>, y corrige sus trampas: no hay tope de comparendos por corrida, no se envía sobre
/// pagados ni inactivos ni a quien ya tiene un DP, y se espera mientras corre el descuento del 50 % de una fotomulta.
/// </summary>
public sealed class SelectorTipologiaDp(ReglasDpAutomatico reglas)
{
    private readonly ReglasDpAutomatico _reglas = reglas ?? throw new ArgumentNullException(nameof(reglas));

    /// <param name="datos">El comparendo.</param>
    /// <param name="hoy">La fecha de hoy (el dominio no lee el reloj).</param>
    /// <param name="disponibles">Códigos que la API permite hoy a esta empresa: habilitados, que proceden en el estado legal
    /// actual y, para el envío, automatizables. Una regla que aplica a una tipología fuera de este conjunto no se elige.</param>
    public DecisionDp Decidir(DatosDeSeleccion datos, DateOnly hoy, IReadOnlySet<string> disponibles)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(disponibles);

        var aplicables = new List<TipologiaAplicable>();
        foreach (var codigo in _reglas.Prioridad)
        {
            if (Razon(codigo, datos, hoy) is { } razon)
                aplicables.Add(new TipologiaAplicable(codigo, razon, disponibles.Contains(codigo)));
        }

        var exclusion = Exclusion(datos);
        if (exclusion is not null)
            return new DecisionDp(null, aplicables, exclusion);

        var elegida = aplicables.FirstOrDefault(a => a.Disponible);
        if (elegida is not null)
            return new DecisionDp(elegida.Codigo, aplicables, null);

        return new DecisionDp(null, aplicables, aplicables.Count == 0 ? MotivoNoElegible.SinTipologiaAplicable : MotivoNoElegible.SinTipologiaDisponible);
    }

    private static MotivoNoElegible? Exclusion(DatosDeSeleccion d) => d switch
    {
        { Activo: false } => MotivoNoElegible.Inactivo,
        { Pagado: true } => MotivoNoElegible.Pagado,
        { TieneDp: true } => MotivoNoElegible.YaTieneDp,
        // Plazos.Tipo trata «no sé si es fotomulta» como fotomulta; aquí se hace igual para no disparar un DP antes de tiempo.
        { EsFotomulta: not false, DescuentoVigente: Descuento.Cincuenta } => MotivoNoElegible.VentanaDescuento,
        _ => null,
    };

    private string? Razon(string codigo, DatosDeSeleccion d, DateOnly hoy) => codigo switch
    {
        CodigosTipologiaDp.Prescripcion => RazonPrescripcion(d, hoy),
        CodigosTipologiaDp.Caducidad => RazonCaducidad(d, hoy),
        CodigosTipologiaDp.RevocatoriaC321 => RazonRevocatoria(d),
        CodigosTipologiaDp.MalaNotificacion => RazonMalaNotificacion(d),
        CodigosTipologiaDp.PersonaJuridica => RazonPersonaJuridica(d),
        _ => null,
    };

    private string? RazonCaducidad(DatosDeSeleccion d, DateOnly hoy)
    {
        var (baseFecha, nombre) = _reglas.BaseCaducidad == BasePlazo.Imposicion
            ? (d.FechaImposicion, "imposición")
            : (d.FechaNotificacion, "notificación");
        if (baseFecha is not { } desde)
            return null;
        var limite = desde.AddYears(_reglas.AniosCaducidad).AddDays(_reglas.DiasExtraCaducidad);
        return limite < hoy
            ? $"Pasó más de {Numero(_reglas.AniosCaducidad)} año desde la {nombre} ({Fecha(desde)}) sin sanción."
            : null;
    }

    private string? RazonMalaNotificacion(DatosDeSeleccion d)
    {
        if (d.FechaImposicion is not { } imposicion || d.FechaNotificacion is not { } notificacion || EsC321(d))
            return null;
        var dias = notificacion.DayNumber - imposicion.DayNumber;
        return dias > _reglas.DiasNotificacionTardia
            ? $"Se notificó {Numero(dias)} días después de la imposición; el límite es {Numero(_reglas.DiasNotificacionTardia)}."
            : null;
    }

    private string? RazonPrescripcion(DatosDeSeleccion d, DateOnly hoy)
    {
        if (d.FechaNotificacion is not { } notificacion)
            return null;
        return notificacion <= hoy.AddYears(-_reglas.AniosPrescripcion)
            ? $"Pasaron {Numero(_reglas.AniosPrescripcion)} años o más desde la notificación ({Fecha(notificacion)})."
            : null;
    }

    private string? RazonPersonaJuridica(DatosDeSeleccion d)
    {
        if (!d.PropietarioEsPersonaJuridica || EsC321(d))
            return null;
        if (_reglas.DllSoloFotomulta && d.EsFotomulta == false)
            return null;
        return "El propietario es una persona jurídica: la sanción no puede recaer en ella sin probar quién conducía.";
    }

    private string? RazonRevocatoria(DatosDeSeleccion d)
    {
        if (!d.PropietarioEsPersonaJuridica || !EsC321(d) || d.FechaImposicion is not { } imposicion)
            return null;
        return imposicion >= _reglas.InicioRevocatoria && imposicion <= _reglas.FinRevocatoria
            ? $"{d.CodigoInfraccion!.Trim().ToUpperInvariant()} impuesta el {Fecha(imposicion)}, dentro del periodo de la sentencia C-321 de 2022."
            : null;
    }

    private bool EsC321(DatosDeSeleccion d) =>
        !string.IsNullOrWhiteSpace(d.CodigoInfraccion) && _reglas.CodigosC321.Contains(d.CodigoInfraccion.Trim());

    private static string Fecha(DateOnly f) => f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Numero(int n) => n.ToString(CultureInfo.InvariantCulture);
}
