namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>HU #14393 (Feature #14392, Épica #14297) — medios de pago del portal de Persona Natural. Los mismos del CHECK de las tablas.</summary>
public static class MediosPagoPn
{
    public const string Pasarela = "PASARELA";
    public const string Pin = "PIN";

    public static readonly IReadOnlyList<string> Todos = [Pasarela, Pin];

    /// <summary>Medios que existen en un portal: el PIN de caja es del punto del organismo y la web no lo tiene.</summary>
    public static IReadOnlyList<string> DelPortal(bool web) => web ? [Pasarela] : Todos;
}

/// <summary>
/// HU #14393 — los dos conceptos de cobro. <c>TRAMITE</c> agrupa derechos de tránsito y las transformaciones (lo fija el
/// organismo); <c>GESTION</c> agrupa servicio FLIT y envío del documento (lo fija FLIT).
/// </summary>
public static class CobrosPn
{
    public const string Tramite = "TRAMITE";
    public const string Gestion = "GESTION";

    public static readonly IReadOnlyList<string> Todos = [Tramite, Gestion];

    /// <summary>Concepto de cobro al que pertenece un concepto de tarifa.</summary>
    public static string DeConcepto(string concepto) =>
        concepto is ConceptosTarifaPn.ServicioFlit or "ENVIO_DOCUMENTO" ? Gestion : Tramite;

    /// <summary>Concepto de tarifa cuyo monto representa a cada concepto de cobro (el renglón base de la cotización).</summary>
    public static string ConceptoBase(string cobro) => cobro == Gestion ? ConceptosTarifaPn.ServicioFlit : ConceptosTarifaPn.DerechosTransito;

    /// <summary>El <c>ot_admin</c> mueve solo el switch del trámite; el SuperAdmin mueve todos.</summary>
    public static bool Mueve(bool esSuperAdmin, string cobro) => esSuperAdmin || cobro == Tramite;
}

public sealed record MedioPnFila(string Method, bool Enabled);

public sealed record SwitchPnFila(Guid TipoTramiteId, string Method, string Charge, bool Enabled);

/// <summary>
/// HU #14393 — lo guardado para UN ámbito (organismo, o la web con <c>web = true</c>) y las reglas que lo interpretan. Una sola
/// fuente para el administrador y para la cotización, así lo que se configura es lo que se cobra:
/// <list type="bullet">
/// <item>Bandera sin fila: la pasarela está encendida (hoy el portal cobra por ella) y el PIN apagado. El PIN no existe en la web.</item>
/// <item>Switch sin fila: vale encendido si hay monto mayor que cero para ese concepto en el tipo y organismo.</item>
/// </list>
/// </summary>
public sealed class ConfigCobroPn
{
    private readonly Dictionary<string, bool> _medios;
    private readonly Dictionary<(Guid, string, string), bool> _switches;

    public ConfigCobroPn(bool web, IEnumerable<MedioPnFila> medios, IEnumerable<SwitchPnFila> switches)
    {
        ArgumentNullException.ThrowIfNull(medios);
        ArgumentNullException.ThrowIfNull(switches);
        Web = web;
        _medios = medios.ToDictionary(m => m.Method, m => m.Enabled, StringComparer.Ordinal);
        _switches = switches.ToDictionary(s => (s.TipoTramiteId, s.Method, s.Charge), s => s.Enabled);
    }

    /// <summary>Sin ninguna fila guardada: el portal cobra como antes de que existiera el configurador.</summary>
    public static ConfigCobroPn Vacia(bool web) => new(web, [], []);

    public bool Web { get; }

    public bool MedioHabilitado(string medio) =>
        MediosPagoPn.DelPortal(Web).Contains(medio) && (_medios.TryGetValue(medio, out var e) ? e : medio == MediosPagoPn.Pasarela);

    /// <summary>Estado del switch: el guardado o, sin fila, encendido si el concepto tiene monto mayor que cero.</summary>
    public bool SwitchEncendido(Guid tipoId, string medio, string cobro, long montoBase) =>
        _switches.TryGetValue((tipoId, medio, cobro), out var e) ? e : montoBase > 0;
}

/// <summary>
/// HU #14393 — persistencia de las banderas y los switches (<c>admin.persona_natural_payment_methods</c> y
/// <c>admin.persona_natural_charge_switches</c>, DDL 182). Tablas globales sin tenant. El ámbito nulo es la web.
/// </summary>
public interface IPersonaNaturalCobroRepository
{
    Task<ConfigCobroPn> LeerAsync(Guid? ambitoOrganismoId, CancellationToken ct = default);

    /// <summary>
    /// Monto vigente del renglón base de cada concepto de cobro por tipo, con la precedencia de la cotización (tipo+organismo,
    /// tipo, general). Con ámbito nulo (web) solo cuentan los valores generales. Un tipo sin monto no aparece.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid TipoId, string Cobro), long>> MontosAsync(Guid? ambitoOrganismoId, CancellationToken ct = default);

    /// <summary>Guarda la bandera y devuelve el valor anterior (nulo si no había fila).</summary>
    Task<bool?> GuardarMedioAsync(Guid? ambitoOrganismoId, string medio, bool enabled, Guid actor, CancellationToken ct = default);

    /// <summary>Guarda el switch y devuelve el valor anterior (nulo si no había fila).</summary>
    Task<bool?> GuardarSwitchAsync(
        Guid? ambitoOrganismoId, Guid tipoId, string medio, string cobro, bool enabled, Guid actor, CancellationToken ct = default);
}
