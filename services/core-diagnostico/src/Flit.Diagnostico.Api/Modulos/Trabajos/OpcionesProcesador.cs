namespace Flit.Diagnostico.Api.Modulos.Trabajos;

/// <summary>
/// Configuración del procesador de la cola de celdas (<c>Diagnostico:Procesador</c>, HU #13727, ADR a). Todo explícito: el
/// procesador no mira <c>IHostEnvironment</c>. Sin <see cref="Habilitado"/>=true el trabajador en segundo plano no se registra
/// (las pruebas lo apagan y llaman al procesador directo). Los valores se validan al arrancar.
/// </summary>
public sealed class OpcionesProcesador
{
    public const string Seccion = "Diagnostico:Procesador";

    /// <summary>Registra el trabajador en segundo plano (por defecto false; appsettings.json lo enciende).</summary>
    public bool Habilitado { get; set; }

    /// <summary>Espera entre tomas cuando la cola está vacía.</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Celdas que toma cada procesador por vuelta (se ejecutan en paralelo).</summary>
    public int TamanoLote { get; set; } = 20;

    /// <summary>Vigencia del lease: si el proceso muere, otra réplica retoma la celda al vencer (ADR a: 2 minutos).</summary>
    public TimeSpan DuracionLease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Vehículos distintos de una misma compañía con celdas en curso a la vez (plan §10: 5).</summary>
    public int VehiculosEnVueloPorEmpresa { get; set; } = 5;

    /// <summary>Lo que está fuera de rango (vacío si todo es válido); el servicio no arranca con un valor inválido.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (Intervalo <= TimeSpan.Zero)
            errores.Add("Intervalo debe ser mayor que cero");
        if (TamanoLote is < 1 or > 500)
            errores.Add("TamanoLote debe estar entre 1 y 500");
        if (DuracionLease < TimeSpan.FromSeconds(10))
            errores.Add("DuracionLease debe ser de al menos 10 segundos");
        if (VehiculosEnVueloPorEmpresa < 1)
            errores.Add("VehiculosEnVueloPorEmpresa debe ser al menos 1");
        return errores;
    }
}
