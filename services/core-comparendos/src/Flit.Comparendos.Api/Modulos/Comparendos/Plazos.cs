using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Domain.Modelo;
using Flit.Comparendos.Domain.Plazos;

namespace Flit.Comparendos.Api.Modulos.Comparendos;

/// <summary>
/// Plazos guardados de un comparendo (§6.1, §6.2). La base es la fecha de notificación [Excel C7]; un comparendo físico
/// sin notificación usa la de imposición (se notifica en la vía, S-40); sin saber si es fotomulta, se trata como
/// fotomulta (S-47).
/// </summary>
public static class Plazos
{
    /// <summary>
    /// HU #13784 (hallazgo B16-1): SIMIT y la fuente municipal escriben 01-01-2000 cuando no tienen la fecha de
    /// notificación. Como fecha real daba comparendos «Prescritos» sin avisos ni descuentos: se trata como ausente.
    /// </summary>
    public static readonly DateOnly FechaVaciaDeLasFuentes = new(2000, 1, 1);

    public static TipoImposicion Tipo(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.EsFotomulta == false ? TipoImposicion.Fisico : TipoImposicion.Fotomulta;
    }

    public static DateOnly? BaseNotificacion(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.FechaNotificacion ?? (c.EsFotomulta == false ? c.FechaImposicion : null);
    }

    public static FechasLegales Fechas(Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.FechaLimiteGestion, c.FechaFinDescuento50, c.FechaFinDescuento25, c.FechaCaducidad, c.FechaPrescripcion);
    }

    /// <summary>Recalcula y guarda las fechas. Devuelve las anteriores si algo cambió; null si quedaron iguales.</summary>
    public static FechasLegales? Recalcular(Comparendo c, MotorDePlazos motor, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(motor);
        var antes = Fechas(c);
        var nuevas = motor.Calcular(Tipo(c), c.FechaImposicion, BaseNotificacion(c));
        c.FechaLimiteGestion = nuevas.LimiteGestion;
        c.FechaFinDescuento50 = nuevas.Limite50;
        c.FechaFinDescuento25 = nuevas.Limite25;
        c.FechaCaducidad = nuevas.Caducidad;
        c.FechaPrescripcion = nuevas.Prescripcion;
        c.PlazosCalculadosEn = ahora;
        return antes == nuevas ? null : antes;
    }

    public static VentanaDescuento Ventana(SituacionLegal s, Comparendo c)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(c);
        if (c.FechaFinDescuento50 is null)
            return VentanaDescuento.SinFechaBase;
        return s.DescuentoVigente switch
        {
            Descuento.Cincuenta => VentanaDescuento.Descuento50,
            Descuento.Veinticinco => VentanaDescuento.Descuento25,
            _ => VentanaDescuento.SinDescuento,
        };
    }
}
