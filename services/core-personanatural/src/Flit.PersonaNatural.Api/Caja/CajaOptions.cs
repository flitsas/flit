namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Portal de caja del organismo (Feature #14453, ADR-0082). Sección <c>Caja</c>; la bandera también se acepta como
/// <c>PersonaNatural:Caja:Habilitado</c>. Apagado por defecto: sin encenderlo no existe ninguna ruta de caja.
/// </summary>
public sealed class CajaOptions
{
    public const string SectionName = "Caja";
    public const string ClaveHabilitadoAlterna = "PersonaNatural:Caja:Habilitado";

    /// <summary>Audiencia (client_id OIDC) del producto caja.</summary>
    public const string Audiencia = "caja";

    /// <summary>Rol del usuario de caja, limitado a un organismo.</summary>
    public const string Rol = "ot_caja";

    public bool Habilitado { get; set; }

    /// <summary>Minutos que un cobro en caja espera, ya pagado, antes de radicarse: es la ventana en que el administrador del OT puede anularlo. 0 radica de inmediato.</summary>
    public int VentanaAnulacionMinutos { get; set; } = 15;

    /// <summary>Búsquedas sin resultado por usuario dentro de <see cref="BusquedasFallidasVentanaMin"/> antes del 429.</summary>
    public int BusquedasFallidasMax { get; set; } = 10;

    public int BusquedasFallidasVentanaMin { get; set; } = 5;

    /// <summary>Tope de la prefactura (PDF, JPG o PNG).</summary>
    public long PrefacturaMaxBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>Tope absoluto de la sesión de caja: un token con más antigüedad se rechaza aunque no haya vencido.</summary>
    public int SesionTopeHoras { get; set; } = 12;

    /// <summary>Cada cuánto el trabajo de fondo radica los cobros cuya ventana venció.</summary>
    public int RadicacionIntervaloSegundos { get; set; } = 30;

    /// <summary>Espera antes del primer ciclo del trabajo de fondo.</summary>
    public int RadicacionArranqueSegundos { get; set; } = 20;

    /// <summary>Solicitudes que un ciclo intenta radicar como máximo.</summary>
    public int RadicacionLote { get; set; } = 50;

    public TimeSpan Ventana => TimeSpan.FromMinutes(Math.Max(0, VentanaAnulacionMinutos));

    public static CajaOptions Leer(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = new CajaOptions();
        configuration.GetSection(SectionName).Bind(o);
        if (configuration.GetSection(SectionName)["Habilitado"] is null && bool.TryParse(configuration[ClaveHabilitadoAlterna], out var alterna))
            o.Habilitado = alterna;
        return o;
    }
}
