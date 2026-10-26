namespace Flit.Diagnostico.Api.Modulos.Consultas.Bus;

/// <summary>
/// Configuración del consumidor de resultados de consultas (<c>Diagnostico:Consultas:Bus</c>, ADR-0075, D1). Sin
/// <see cref="ConsumidorHabilitado"/>=true el consumidor de la cola <see cref="Cola"/> no se registra (las pruebas lo apagan y
/// entregan los resultados con su propio bus) y tampoco el reconciliador. <see cref="Concurrencia"/> mensajes a la vez, con
/// <see cref="Prefetch"/> en vuelo (la concurrencia no puede pasar del prefetch). Todo explícito: no mira <c>IHostEnvironment</c>.
/// Los valores se validan al arrancar.
/// </summary>
public sealed class OpcionesBus
{
    public const string Seccion = "Diagnostico:Consultas:Bus";

    /// <summary>Cola de Diagnóstico atada a <c>flit.consultas</c> con la llave <see cref="ResultadoConsultaDiagnostico.Tipo"/>.</summary>
    public const string Cola = "diagnostico.consultas-resultados";

    /// <summary>Único productor que la cola acepta (<c>user_id</c> validado por el broker, Q3 = 3a).</summary>
    public const string Productor = "consultas";

    /// <summary>Registra el consumidor y el reconciliador (por defecto false; appsettings.json lo enciende).</summary>
    public bool ConsumidorHabilitado { get; set; }

    /// <summary>Resultados que se atienden a la vez (diseño Q5: 4).</summary>
    public ushort Concurrencia { get; set; } = 4;

    /// <summary>Resultados en vuelo por consumidor (diseño Q5: 8).</summary>
    public ushort Prefetch { get; set; } = 8;

    /// <summary>Lo que está fuera de rango (vacío si todo es válido); el servicio no arranca con un valor inválido.</summary>
    public IReadOnlyList<string> Errores()
    {
        var errores = new List<string>();
        if (Concurrencia < 1)
            errores.Add("Concurrencia debe ser al menos 1");
        if (Prefetch < Concurrencia)
            errores.Add("Prefetch no puede ser menor que Concurrencia");
        return errores;
    }
}
