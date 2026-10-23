using Flit.Diagnostico.Domain.Consultas;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>Adaptador que atiende las fuentes de core-consultas en este despliegue (HU #13791, F05-C2).</summary>
public enum ModoAdaptador
{
    /// <summary>Respuestas inventadas y deterministas; solo en LOCAL o DEV. Ninguna llamada sale a core-consultas.</summary>
    Simulado,

    /// <summary>Llamada a core-consultas por gRPC (<see cref="ClienteConsultas"/>).</summary>
    Real,

    /// <summary>Falla cerrado: error definitivo <c>NO_CONECTADO</c>, nunca datos; la fuente no se ofrece.</summary>
    NoConectado,
}

/// <summary>
/// Regla única que elige, al arrancar, el adaptador de las fuentes de core-consultas (HU #13791, F05-C2; regla de #13725):
/// <list type="number">
/// <item>Con <see cref="Modulos.ClaveSimulacionHabilitada"/>=true, todas son simuladas, y solo se admite con
/// <see cref="Modulos.ClaveAmbiente"/> LOCAL o DEV: en otro ambiente (o sin definir) el servicio no arranca. La simulación
/// manda sobre la dirección: los documentos simulados son inventados y nunca deben llegar a un proveedor real.</item>
/// <item>Si no, con <see cref="ClienteConsultas.ClaveDireccion"/> (URL http o https), todas son reales; una dirección que no es
/// URL impide arrancar.</item>
/// <item>Si no, todas son no conectadas: <see cref="EsNoConectada"/> es true y <see cref="FuentesDisponibles"/> no las ofrece.</item>
/// </list>
/// RUNT (<see cref="Fuente.Runt"/>) conserva su regla: simulada con la simulación y no conectada en otro caso, hasta que llegue su
/// adaptador real (C-PER); nunca simulada junto con fuentes reales. <see cref="Fuente.ImpuestoDepartamental"/> (HU #13862) sigue
/// este mismo modo: la bandera <c>Diagnostico:Fuentes:ImpuestoDisponible</c> no elige adaptador, solo decide su disponibilidad
/// (F09-H4). No mira <c>IHostEnvironment</c>: los tres ambientes corren como Development.
/// </summary>
/// <example>
/// <code>
/// var seleccion = SeleccionAdaptadores.Desde(configuration);
/// services.AddAdaptador&lt;IFuenteSimit, FuenteSimitReal, FuenteSimitSimulada, FuenteSimitNoConectada&gt;(seleccion);
/// </code>
/// </example>
public sealed class SeleccionAdaptadores
{
    private static readonly string[] AmbientesConSimulacion = ["LOCAL", "DEV"];

    private SeleccionAdaptadores(ModoAdaptador modo, Uri? direccion, TimeSpan deadline)
    {
        Modo = modo;
        Direccion = direccion;
        Deadline = deadline;
    }

    /// <summary>Fuentes que se consultan por core-consultas con la regla de esta clase (F05; el impuesto desde HU #13862).</summary>
    public static IReadOnlyList<Fuente> FuentesPorConsultas { get; } =
    [
        Fuente.RuntPropietario, Fuente.RuntLocatario, Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario, Fuente.Avaluo,
        Fuente.ImpuestoDepartamental,
    ];

    /// <summary>Modo de las fuentes de <see cref="FuentesPorConsultas"/>.</summary>
    public ModoAdaptador Modo { get; }

    /// <summary>Dirección gRPC de core-consultas; solo en <see cref="ModoAdaptador.Real"/>.</summary>
    public Uri? Direccion { get; }

    /// <summary>Deadline de cada llamada a core-consultas (<see cref="ClienteConsultas.ClaveDeadline"/>, por defecto 90 s).</summary>
    public TimeSpan Deadline { get; }

    /// <summary>Lee y valida la configuración; lanza <see cref="InvalidOperationException"/> si el servicio no debe arrancar.</summary>
    public static SeleccionAdaptadores Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var segundos = configuration.GetValue(ClienteConsultas.ClaveDeadline, ClienteConsultas.DeadlinePorDefectoSegundos);
        if (segundos <= 0)
        {
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(ClienteConsultas.ClaveDeadline)} debe ser mayor que 0 (valor actual: {segundos}).");
        }

        var deadline = TimeSpan.FromSeconds(segundos);
        if (configuration.GetValue(Modulos.ClaveSimulacionHabilitada, false))
        {
            var ambiente = configuration[Modulos.ClaveAmbiente]?.Trim();
            if (!AmbientesConSimulacion.Contains(ambiente, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(Modulos.ClaveSimulacionHabilitada)}=true solo se admite en LOCAL o DEV " +
                    $"({Variable(Modulos.ClaveAmbiente)}=DEV o LOCAL; valor actual: «{(string.IsNullOrEmpty(ambiente) ? "sin definir" : ambiente)}»). " +
                    $"En QA y PDN quite {Variable(Modulos.ClaveSimulacionHabilitada)}: las fuentes simuladas usan documentos inventados. " +
                    "Ver services/core-diagnostico/README.md.");
            }

            return new SeleccionAdaptadores(ModoAdaptador.Simulado, null, deadline);
        }

        var direccion = configuration[ClienteConsultas.ClaveDireccion]?.Trim();
        if (string.IsNullOrEmpty(direccion))
            return new SeleccionAdaptadores(ModoAdaptador.NoConectado, null, deadline);

        if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(ClienteConsultas.ClaveDireccion)} no es una URL http o https " +
                "(p. ej. http://core-consultas:8084). Sin dirección, las fuentes de core-consultas quedan no conectadas.");
        }

        return new SeleccionAdaptadores(ModoAdaptador.Real, uri, deadline);
    }

    /// <summary>
    /// Modo de una fuente: el de <see cref="FuentesPorConsultas"/> (incluido el impuesto, HU #13862); para RUNT, simulado con la
    /// simulación y no conectado en otro caso (#13725); null para una fuente fuera de esta regla.
    /// </summary>
    public ModoAdaptador? ModoDe(Fuente fuente) => fuente switch
    {
        Fuente.Runt => Modo == ModoAdaptador.Simulado ? ModoAdaptador.Simulado : ModoAdaptador.NoConectado,
        _ when FuentesPorConsultas.Contains(fuente) => Modo,
        _ => null,
    };

    /// <summary>
    /// Verdadero si la fuente es de core-consultas y su adaptador es el no conectado: no se ofrece (422
    /// <c>FUENTE_NO_DISPONIBLE</c>). RUNT nunca: su disponibilidad la decide su ejecutor (#13733).
    /// </summary>
    public bool EsNoConectada(Fuente fuente) => Modo == ModoAdaptador.NoConectado && FuentesPorConsultas.Contains(fuente);

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);
}

/// <summary>
/// Registro de los tres adaptadores de un puerto de fuente según <see cref="SeleccionAdaptadores"/> (HU #13791). Lo llaman
/// F05-H3..H6 desde <c>Modulos.AddFuentesConsultas</c>, una línea por puerto. Scoped (el real depende de
/// <see cref="ClienteConsultas"/>, que es scoped) y con TryAdd, para que las pruebas pongan su doble antes.
/// </summary>
public static class RegistroPorSeleccion
{
    public static IServiceCollection AddAdaptador<TPuerto, TReal, TSimulado, TNoConectado>(this IServiceCollection services, SeleccionAdaptadores seleccion)
        where TPuerto : class
        where TReal : class, TPuerto
        where TSimulado : class, TPuerto
        where TNoConectado : class, TPuerto
    {
        ArgumentNullException.ThrowIfNull(seleccion);
        switch (seleccion.Modo)
        {
            case ModoAdaptador.Simulado:
                services.TryAddScoped<TPuerto, TSimulado>();
                break;
            case ModoAdaptador.Real:
                services.TryAddScoped<TPuerto, TReal>();
                break;
            default:
                services.TryAddScoped<TPuerto, TNoConectado>();
                break;
        }

        return services;
    }
}
