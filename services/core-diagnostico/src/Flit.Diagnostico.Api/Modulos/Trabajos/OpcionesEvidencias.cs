namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Configuración de la cola del ZIP del lote (<c>Diagnostico:Evidencias</c>, HU #13850, decisiones Q2 y Q5 del 2026-10-22). Todo
/// explícito, como <see cref="OpcionesProcesador"/>: sin <see cref="Habilitado"/>=true el trabajador en segundo plano no se registra
/// (las pruebas lo apagan y llaman al empaquetador directo). Los valores se validan al arrancar.
/// </summary>
public sealed class OpcionesEvidencias
{
    public const string Seccion = "Diagnostico:Evidencias";

    /// <summary>Registra el trabajador en segundo plano (por defecto false; appsettings.json lo enciende).</summary>
    public bool Habilitado { get; set; }

    /// <summary>Espera entre tomas cuando la cola está vacía.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Vigencia del lease de un ZIP en generación: si el proceso muere, otra réplica lo retoma al vencer. Mientras genera, el
    /// empaquetador lo renueva al pasar la mitad.
    /// </summary>
    public TimeSpan DuracionLease { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Reintentos tras un fallo de generación o del almacén (por defecto 2: tres intentos en total); agotados, ERROR.</summary>
    public int MaxReintentos { get; set; } = 2;

    /// <summary>Espera antes del primer reintento; se duplica en cada uno (backoff).</summary>
    public TimeSpan EsperaReintento { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Carpeta del archivo temporal del ZIP (por defecto la temporal del sistema). Se borra siempre al terminar.</summary>
    public string? DirectorioTemporal { get; set; }

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
        if (EsperaReintento <= TimeSpan.Zero)
            errores.Add("EsperaReintento debe ser mayor que cero");
        return errores;
    }

    /// <summary>La carpeta donde se escribe el ZIP temporal.</summary>
    public string CarpetaTemporal() => string.IsNullOrWhiteSpace(DirectorioTemporal) ? Path.GetTempPath() : DirectorioTemporal;
}
