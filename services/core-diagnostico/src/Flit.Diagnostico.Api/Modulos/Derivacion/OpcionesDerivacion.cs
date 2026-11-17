namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// <c>Diagnostico:Derivacion</c> (Punto 4, diseño §5.3 «Configuración», ADR-0084). Apagada por defecto: sin <see cref="Habilitada"/> no
/// se mapea «Crear trámite» (individual ni masivo), la grilla trae <c>derivacion: null</c> y no se registran el consumidor ni el
/// reconciliador. <see cref="PlazoRpcSegundos"/> es el deadline del rpc <c>CrearBorradorDesdeDiagnostico</c> (1..120).
/// <see cref="UrlTramites"/> es la base de la app de Trámites para el enlace «Ver trámite #N» (<c>{base}/tramites/{id}?t={empresa}</c>);
/// vacía, el enlace es relativo y lo completa el front.
/// <para>
/// Masivo (incremento 3): consumidor de <c>diagnostico.derivaciones</c> con <see cref="Concurrencia"/> y <see cref="Prefetch"/> por
/// réplica (techo global = réplicas × Concurrencia; core-api <c>Tramites:Derivacion:ConcurrenciaMaxima</c> debe ser ≥ esa suma),
/// <see cref="CreditoPorCompania"/> ítems en vuelo por compañía (≤ Concurrencia), freno con <see cref="TasaPorSegundo"/> y pausa de
/// <see cref="PausaSegundos"/> tras <see cref="PausaTrasFallos"/> transitorios seguidos, <see cref="MaxIntentos"/> llamadas por ítem
/// (1..3, una por espera del SDK) y <see cref="MaxItemsPorOperacion"/> vehículos por envío. <see cref="ConsumidorHabilitado"/> (por
/// defecto true) permite tener las rutas sin el consumidor (las pruebas lo apagan y entregan el evento a mano). Todo se valida al
/// arrancar (<see cref="Errores"/>).
/// </para>
/// </summary>
public sealed class OpcionesDerivacion
{
    public const string Seccion = "Diagnostico:Derivacion";

    /// <summary>Máximo de intentos automáticos: uno por espera de la cola de reintentos (<see cref="ReintentoDerivacion.Esperas"/>).</summary>
    public const int MaxIntentosPermitido = 3;

    public bool Habilitada { get; set; }

    public int PlazoRpcSegundos { get; set; } = 15;

    public string UrlTramites { get; set; } = string.Empty;

    /// <summary>Registra el consumidor y el reconciliador (solo si además <see cref="Habilitada"/>).</summary>
    public bool ConsumidorHabilitado { get; set; } = true;

    /// <summary>Mensajes que el consumidor atiende a la vez por réplica (diseño: 4).</summary>
    public ushort Concurrencia { get; set; } = 4;

    /// <summary>Mensajes en vuelo por consumidor (diseño: 8); no menor que <see cref="Concurrencia"/>.</summary>
    public ushort Prefetch { get; set; } = 8;

    /// <summary>Ítems ENVIADO a la vez por compañía (diseño: 2); entre 1 y <see cref="Concurrencia"/>.</summary>
    public int CreditoPorCompania { get; set; } = 2;

    /// <summary>Llamadas por segundo al rpc por réplica (diseño: 10); la ráfaga es <see cref="Concurrencia"/>.</summary>
    public int TasaPorSegundo { get; set; } = 10;

    /// <summary>Transitorios seguidos que abren la pausa del freno (diseño: 5).</summary>
    public int PausaTrasFallos { get; set; } = 5;

    /// <summary>Duración de la pausa del freno (diseño: 30 s).</summary>
    public int PausaSegundos { get; set; } = 30;

    /// <summary>Llamadas al rpc por ítem antes de quedar FALLIDO (diseño: 3).</summary>
    public int MaxIntentos { get; set; } = 3;

    /// <summary>Vehículos por envío del masivo (diseño: 5000).</summary>
    public int MaxItemsPorOperacion { get; set; } = 5000;

    public OpcionesReconciliadorDerivacion Reconciliador { get; set; } = new();

    /// <summary>Plazo entre 1 y 120 s, URL de Trámites absoluta http(s) o vacía y las claves del masivo en rango.</summary>
    public static bool Validar(OpcionesDerivacion opciones) => Errores(opciones).Count == 0;

    /// <summary>Lo que está fuera de rango (vacío si todo es válido).</summary>
    public static IReadOnlyList<string> Errores(OpcionesDerivacion opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var errores = new List<string>();
        if (opciones.PlazoRpcSegundos is < 1 or > 120)
            errores.Add("PlazoRpcSegundos debe estar entre 1 y 120");
        if (!string.IsNullOrWhiteSpace(opciones.UrlTramites)
            && !(Uri.TryCreate(opciones.UrlTramites, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)))
            errores.Add("UrlTramites debe ser una URL absoluta http(s) o vacía");
        if (opciones.Concurrencia < 1)
            errores.Add("Concurrencia debe ser al menos 1");
        if (opciones.Prefetch < opciones.Concurrencia)
            errores.Add("Prefetch no puede ser menor que Concurrencia");
        if (opciones.CreditoPorCompania < 1 || opciones.CreditoPorCompania > Math.Max((int)opciones.Concurrencia, 1))
            errores.Add("CreditoPorCompania debe estar entre 1 y Concurrencia");
        if (opciones.TasaPorSegundo is < 1 or > 10_000)
            errores.Add("TasaPorSegundo debe estar entre 1 y 10000");
        if (opciones.PausaTrasFallos < 1)
            errores.Add("PausaTrasFallos debe ser al menos 1");
        if (opciones.PausaSegundos is < 1 or > 3600)
            errores.Add("PausaSegundos debe estar entre 1 y 3600");
        if (opciones.MaxIntentos is < 1 or > MaxIntentosPermitido)
            errores.Add($"MaxIntentos debe estar entre 1 y {MaxIntentosPermitido}");
        if (opciones.MaxItemsPorOperacion is < 1 or > 50_000)
            errores.Add("MaxItemsPorOperacion debe estar entre 1 y 50000");
        var r = opciones.Reconciliador ?? new();
        if (r.Intervalo <= TimeSpan.Zero)
            errores.Add("Reconciliador:Intervalo debe ser mayor que cero");
        if (r.Margen < TimeSpan.FromMinutes(1))
            errores.Add("Reconciliador:Margen debe ser al menos 1 min");
        if (r.Maximo is < 1 or > 5000)
            errores.Add("Reconciliador:Maximo debe estar entre 1 y 5000");
        return errores;
    }

    /// <summary>Enlace al borrador en Trámites (<c>/tramites/{id}?t={empresa}</c>, como <c>LogQx</c> del front de Trámites).</summary>
    public string UrlTramite(Guid tramite, Guid empresa) =>
        $"{(UrlTramites ?? string.Empty).TrimEnd('/')}/tramites/{tramite}?t={empresa}";
}

/// <summary>
/// <c>Diagnostico:Derivacion:Reconciliador</c> (diseño §5.3 punto 7): cada <see cref="Intervalo"/> (60 s) busca hasta
/// <see cref="Maximo"/> ítems ENVIADO sin progreso tras <see cref="Margen"/> (15 min).
/// </summary>
public sealed class OpcionesReconciliadorDerivacion
{
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan Margen { get; set; } = TimeSpan.FromMinutes(15);

    public int Maximo { get; set; } = 500;
}
