namespace Flit.Impuestos.Api.Modulos.Conciliacion;

/// <summary>
/// Calendario puro del respaldo por GET (plan §2.2, S12 ajustado a DA-6=c, sin vencimiento): con webhook, primer GET a
/// <see cref="OpcionesConciliacion.PrimerGetConWebhook"/> del envío, luego cada <see cref="OpcionesConciliacion.IntervaloConWebhook"/>
/// hasta <see cref="OpcionesConciliacion.HastaIntervaloLargo"/> y después cada <see cref="OpcionesConciliacion.IntervaloLargo"/>,
/// para siempre; sin webhook, cada <see cref="OpcionesConciliacion.IntervaloSinWebhook"/> durante
/// <see cref="OpcionesConciliacion.DuracionSinWebhook"/> y luego el calendario anterior. Quien lo usa deja de consultar cuando el
/// ítem llega a estado final o su lote se cierra.
/// </summary>
/// <example>
/// <code>
/// item.ProximoPollEn = CalendarioConciliacion.Primero(item.EnviadoEn!.Value, CalendarioConciliacion.SinWebhook(item.ConCallback, modoRespaldo), opciones);
/// </code>
/// </example>
public static class CalendarioConciliacion
{
    /// <summary>Sin webhook: la consulta salió sin <c>callbackUrl</c> o la compañía está en modo respaldo de firma.</summary>
    public static bool SinWebhook(bool conCallback, bool modoRespaldo) => !conCallback || modoRespaldo;

    /// <summary>El primer GET tras el 202 (M09 lo guarda en <c>proximo_poll_en</c> al pasar a <c>ENVIADO</c>).</summary>
    public static DateTimeOffset Primero(DateTimeOffset enviadoEn, bool sinWebhook, OpcionesConciliacion o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return enviadoEn + (sinWebhook ? o.IntervaloSinWebhook : o.PrimerGetConWebhook);
    }

    /// <summary>El siguiente GET después de uno hecho en <paramref name="ahora"/> que no trajo resultado final.</summary>
    public static DateTimeOffset Siguiente(DateTimeOffset enviadoEn, DateTimeOffset ahora, bool sinWebhook, OpcionesConciliacion o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var transcurrido = ahora - enviadoEn;
        if (sinWebhook && transcurrido < o.DuracionSinWebhook)
            return ahora + o.IntervaloSinWebhook;
        if (!sinWebhook && transcurrido < o.PrimerGetConWebhook)
            return enviadoEn + o.PrimerGetConWebhook;
        return transcurrido < o.HastaIntervaloLargo ? ahora + o.IntervaloConWebhook : ahora + o.IntervaloLargo;
    }
}
