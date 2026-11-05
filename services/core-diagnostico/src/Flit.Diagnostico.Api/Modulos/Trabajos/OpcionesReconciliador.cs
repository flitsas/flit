namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Configuración del reconciliador de consultas (<c>Diagnostico:Consultas:Reconciliador</c>, ADR-0075, D3, 6r-i, U4 = a, Dec. 6).
/// Corre cada <see cref="Intervalo"/> (60 s). Una celda con trabajo y sin resultado tras <see cref="Margen"/> (30 min) solo cuenta
/// como perdida si core-consultas ya devolvió resultados de trabajos publicados en su misma cola más de
/// <see cref="ToleranciaOrden"/> (15 min) después que el suyo (marca de agua FIFO): un lote grande que espera su turno no se toca.
/// Aun perdida, no se republica ni se cierra si core-consultas no devolvió ningún resultado en la última <see cref="VentanaVida"/>
/// (10 min). Pasados <see cref="TtlTrabajo"/> (24 h) + <see cref="Margen"/> desde la publicación, el mensaje ya venció en el broker
/// y la celda se cierra en ERROR. Revisa a lo sumo <see cref="Maximo"/> celdas por vuelta. Se registra con el consumidor de
/// resultados (<c>Diagnostico:Consultas:Bus:ConsumidorHabilitado</c>). Los valores se validan al arrancar.
/// </summary>
/// <example>
/// <code>
/// "Diagnostico": { "Consultas": { "Reconciliador": { "TtlTrabajo": "1.00:00:00", "ToleranciaOrden": "00:15:00" } } }
/// </code>
/// </example>
public sealed class OpcionesReconciliador
{
    public const string Seccion = "Diagnostico:Consultas:Reconciliador";

    /// <summary>Espera entre vueltas.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Tiempo sin resultado tras el que una celda con trabajo es huérfana.</summary>
    public TimeSpan Margen { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Si core-consultas no devolvió ningún resultado en este tiempo, el reconciliador no republica ni cierra.</summary>
    public TimeSpan VentanaVida { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Desorden aceptado en la cola FIFO: una celda está perdida solo si core-consultas devolvió un trabajo de su cola publicado más
    /// de este tiempo después que el suyo. Cubre la concurrencia (8 lote / 4 Express, cada consulta de segundos a pocos minutos), la
    /// cola de resultados (4) y los reintentos del SDK en core-consultas ante una falla de su infraestructura (10 s + 1 min + 10 min,
    /// que sacan el mensaje del orden hasta ~11 min). Más alta solo retrasa la detección de una pérdida; más baja duplica consultas.
    /// </summary>
    public TimeSpan ToleranciaOrden { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// TTL de las colas principales en el broker (<c>message-ttl</c> de las políticas <c>ttl-*</c> de
    /// <c>deploy/rabbitmq/definitions.json</c>; deben coincidir): un trabajo que no se consumió en ese tiempo salió a la DLQ.
    /// </summary>
    public TimeSpan TtlTrabajo { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Celdas que revisa por vuelta.</summary>
    public int Maximo { get; set; } = 200;

    /// <summary>Lo que está fuera de rango (vacío si todo es válido); el servicio no arranca con un valor inválido.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (Intervalo <= TimeSpan.Zero)
            errores.Add("Intervalo debe ser mayor que cero");
        if (Margen < TimeSpan.FromMinutes(1))
            errores.Add("Margen debe ser de al menos 1 minuto");
        if (VentanaVida <= TimeSpan.Zero)
            errores.Add("VentanaVida debe ser mayor que cero");
        if (Maximo is < 1 or > 5000)
            errores.Add("Maximo debe estar entre 1 y 5000");
        if (TtlTrabajo <= Margen)
            errores.Add("TtlTrabajo debe ser mayor que Margen");
        if (ToleranciaOrden < TimeSpan.FromMinutes(1))
            errores.Add("ToleranciaOrden debe ser de al menos 1 minuto");
        else if (ToleranciaOrden >= TtlTrabajo)
            errores.Add("ToleranciaOrden debe ser menor que TtlTrabajo");
        return errores;
    }
}
