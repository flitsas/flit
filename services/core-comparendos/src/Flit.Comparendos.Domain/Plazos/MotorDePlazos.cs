using Flit.Comparendos.Domain.Calendario;

namespace Flit.Comparendos.Domain.Plazos;

/// <summary>Cómo se impuso el comparendo: lo trae la fuente (fotodetección S/N); lo que entra por OCR es físico (Excel C5).</summary>
public enum TipoImposicion
{
    /// <summary>Lo impone un agente de tránsito en la vía.</summary>
    Fisico = 1,

    /// <summary>Lo detecta una cámara y se notifica después al propietario.</summary>
    Fotomulta = 2,
}

/// <summary>Estado legal, calculado al consultar y nunca guardado (ADO #12878, MOD03 RN-07).</summary>
public enum EstadoLegal
{
    /// <summary>Sin fecha de notificación todavía: no corre ningún plazo (Excel C7).</summary>
    SinNotificar = 0,
    EnTiempo = 1,
    Vencido = 2,
    Caducado = 3,
    Prescrito = 4,
}

/// <summary>Descuento por pronto pago vigente hoy (Excel C9).</summary>
public enum Descuento
{
    Ninguno = 0,
    Cincuenta = 50,
    Veinticinco = 25,
}

/// <summary>Fechas fijas de un comparendo; se calculan al crearlo y cada vez que la fuente cambia una fecha (Excel C2).</summary>
public sealed record FechasLegales(
    DateOnly? LimiteGestion,
    DateOnly? Limite50,
    DateOnly? Limite25,
    DateOnly? Caducidad,
    DateOnly? Prescripcion);

/// <summary>Lo que muestra el semáforo de un comparendo en una fecha dada.</summary>
public sealed record SituacionLegal(
    EstadoLegal Estado,
    int? HabilesRestantesGestion,
    Descuento DescuentoVigente,
    DateOnly? FinDescuentoVigente,
    int? HabilesRestantesDescuento,
    bool ProximoAVencer);

/// <summary>
/// Plazos de un comparendo. Reglas (fuentes entre corchetes):
/// <list type="bullet">
/// <item>Los días se cuentan hábiles: sin sábados, domingos ni festivos [Excel C6].</item>
/// <item>Todo plazo corre desde la fecha de notificación [Excel C7]; sin ella no corre ninguno.</item>
/// <item>Ventana de gestión: 11 días hábiles [ADO #12878].</item>
/// <item>Descuentos [Excel C9]: físico, desde la imposición, 50% hasta el hábil 5 y 25% del 6 al 20; fotomulta, desde
/// la notificación, 50% hasta el hábil 11 y 25% del 12 al 26. Después, 100%.</item>
/// <item>Caducidad 1 año y prescripción 3 años [ADO #12878], desde la notificación [Excel C7]; si caen en día no hábil,
/// se corren al siguiente hábil (RN-074).</item>
/// <item>Próximo a vencer: a 2 días hábiles o menos del límite de gestión [ADO #12982 «48 horas», en hábiles por C6].</item>
/// <item>Cada estado empieza el día después de su fecha límite (§6.1: hoy &gt; fecha).</item>
/// </list>
/// </summary>
public sealed class MotorDePlazos(CalendarioHabil calendario)
{
    public const int DiasVentanaGestion = 11;
    public const int DiasProximoAVencer = 2;

    private readonly CalendarioHabil _calendario = calendario ?? throw new ArgumentNullException(nameof(calendario));

    public static (int Hasta50, int Hasta25) TramosDeDescuento(TipoImposicion tipo) =>
        tipo == TipoImposicion.Fisico ? (5, 20) : (11, 26);

    public FechasLegales Calcular(TipoImposicion tipo, DateOnly? fechaImposicion, DateOnly? fechaNotificacion)
    {
        // §6.2: el físico cuenta el descuento desde la imposición; sin esa fecha no hay ventana de descuento.
        var anclaDescuento = tipo == TipoImposicion.Fisico ? fechaImposicion : fechaNotificacion;
        var (hasta50, hasta25) = TramosDeDescuento(tipo);

        return new FechasLegales(
            fechaNotificacion is { } n ? _calendario.SumarHabiles(n, DiasVentanaGestion) : null,
            anclaDescuento is { } a50 ? _calendario.SumarHabiles(a50, hasta50) : null,
            anclaDescuento is { } a25 ? _calendario.SumarHabiles(a25, hasta25) : null,
            fechaNotificacion is { } c ? _calendario.HabilOSiguiente(c.AddYears(1)) : null,
            fechaNotificacion is { } p ? _calendario.HabilOSiguiente(p.AddYears(3)) : null);
    }

    public SituacionLegal Evaluar(FechasLegales fechas, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(fechas);

        var (descuento, finDescuento) = fechas switch
        {
            { Limite50: { } l50 } when hoy <= l50 => (Descuento.Cincuenta, (DateOnly?)l50),
            { Limite25: { } l25 } when hoy <= l25 => (Descuento.Veinticinco, (DateOnly?)l25),
            _ => (Descuento.Ninguno, (DateOnly?)null),
        };
        int? restantesDescuento = finDescuento is { } fin ? _calendario.HabilesRestantes(hoy, fin) : null;

        if (fechas.LimiteGestion is not { } limite)
            return new SituacionLegal(EstadoLegal.SinNotificar, null, descuento, finDescuento, restantesDescuento, false);

        var estado = hoy switch
        {
            _ when fechas.Prescripcion is { } p && hoy > p => EstadoLegal.Prescrito,
            _ when fechas.Caducidad is { } c && hoy > c => EstadoLegal.Caducado,
            _ when hoy > limite => EstadoLegal.Vencido,
            _ => EstadoLegal.EnTiempo,
        };
        var restantes = estado == EstadoLegal.EnTiempo ? _calendario.HabilesRestantes(hoy, limite) : 0;
        return new SituacionLegal(
            estado,
            restantes,
            descuento,
            finDescuento,
            restantesDescuento,
            estado == EstadoLegal.EnTiempo && restantes <= DiasProximoAVencer);
    }
}
