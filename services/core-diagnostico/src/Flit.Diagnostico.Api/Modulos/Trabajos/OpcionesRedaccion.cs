namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Configuración de la cola de redacción (<c>Diagnostico:Redaccion</c>, corte 6). Sin <see cref="Habilitado"/>=true el
/// trabajador en segundo plano no se registra. Los valores se validan al arrancar.
/// </summary>
public sealed class OpcionesRedaccion
{
    public const string Seccion = "Diagnostico:Redaccion";

    /// <summary>Registra el trabajador en segundo plano (por defecto false; appsettings.json lo enciende).</summary>
    public bool Habilitado { get; set; }

    /// <summary>Espera entre tomas cuando la cola está vacía.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Vigencia del lease de una redacción tomada. Si el proceso muere, otra réplica la retoma al vencer.</summary>
    public TimeSpan DuracionLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reintentos tras un fallo técnico (por defecto 2). Pasado ese número, la fila sigue pendiente y ya no se agenda.</summary>
    public int MaxReintentos { get; set; } = 2;

    /// <summary>Lo que está fuera de rango (vacío si todo es válido); el servicio no arranca con un valor inválido.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (Intervalo <= TimeSpan.Zero)
            errores.Add("Intervalo debe ser mayor que cero");
        if (DuracionLease < TimeSpan.FromSeconds(30))
            errores.Add("DuracionLease debe ser de al menos 30 segundos");
        if (MaxReintentos is < 0 or > 5)
            errores.Add("MaxReintentos debe estar entre 0 y 5");
        return errores;
    }
}
