namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Configuración del reconciliador de consultas (<c>Diagnostico:Consultas:Reconciliador</c>, ADR-0075, D3, 6r-i, U4 = a). Corre
/// cada <see cref="Intervalo"/> (60 s); una celda es huérfana si lleva más de <see cref="Margen"/> (30 min) sin resultado, y no se
/// republica ni se cierra si core-consultas no devolvió ningún resultado en la última <see cref="VentanaVida"/> (10 min): los
/// trabajos están esperando en la cola, no perdidos. Revisa a lo sumo <see cref="Maximo"/> celdas por vuelta. Se registra con el
/// consumidor de resultados (<c>Diagnostico:Consultas:Bus:ConsumidorHabilitado</c>). Los valores se validan al arrancar.
/// </summary>
public sealed class OpcionesReconciliador
{
    public const string Seccion = "Diagnostico:Consultas:Reconciliador";

    /// <summary>Espera entre vueltas.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Tiempo sin resultado tras el que una celda con trabajo es huérfana.</summary>
    public TimeSpan Margen { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Si core-consultas no devolvió ningún resultado en este tiempo, el reconciliador no republica ni cierra.</summary>
    public TimeSpan VentanaVida { get; set; } = TimeSpan.FromMinutes(10);

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
        return errores;
    }
}
