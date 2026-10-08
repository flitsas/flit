using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13417 (épica #13216, ADR-0070 adenda v7) — alcance de red con el que se crea un lote desde la vista de red de una
/// cabeza: el <see cref="TenantScope"/> efectivo (el de grupo que resolvió <c>TenantEnforcementMiddleware</c> desde la
/// BD, o el de una sola hija tras <see cref="NetworkScopePolicy.Narrow"/>) y la hija acotada. Nunca sale del cuerpo:
/// lo fabrica <see cref="LoteAlcanceRedPolicy.EvaluarAsync"/> después de las puertas de red.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var red = new LoteAlcanceRed(TenantScope.Group(cabeza, hijas, GroupKind.MarcaBlanca), hijaId: null); // Resumen = "red"
/// var hija = new LoteAlcanceRed(TenantScope.Single(c1), c1);                                         // Resumen = "hija"
/// </code>
/// </remarks>
public sealed record LoteAlcanceRed
{
    /// <summary><c>filter_summary.alcanceRed</c> y <c>LoteConsolidados.alcanceRed</c>: toda la red.</summary>
    public const string ResumenRed = "red";

    /// <summary><c>filter_summary.alcanceRed</c> y <c>LoteConsolidados.alcanceRed</c>: una hija (sin su id ni su nombre).</summary>
    public const string ResumenHija = "hija";

    /// <param name="alcance">Alcance efectivo: grupo (toda la red) o el de la hija acotada.</param>
    /// <param name="hijaId"><c>null</c> = toda la red; si no, la hija, que debe ser el único tenant del alcance.</param>
    public LoteAlcanceRed(TenantScope alcance, Guid? hijaId)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        if (alcance.IsAll || alcance.ReadTenantIds.Count == 0)
            throw new ArgumentException("El alcance de red nunca es el total ni vacío.", nameof(alcance));
        if (hijaId is { } hija && (hija == Guid.Empty || alcance.ReadTenantIds.Count != 1 || !alcance.CanRead(hija)))
            throw new ArgumentException("Con una hija acotada, el alcance es exactamente esa hija (Narrow).", nameof(hijaId));
        if (hijaId is null && !alcance.IsGroup)
            throw new ArgumentException("Toda la red exige el alcance de grupo de la cabeza.", nameof(alcance));

        Alcance = alcance;
        HijaId = hijaId;
    }

    /// <summary>Compañías que el lote puede leer (<see cref="TenantScope.ReadTenantIds"/>): {cabeza} ∪ hijas, o la hija.</summary>
    public TenantScope Alcance { get; }

    /// <summary>Hija acotada (<c>batches.scope_tenant_id</c>); <c>null</c> = toda la red.</summary>
    public Guid? HijaId { get; }

    /// <summary><see cref="ResumenRed"/> o <see cref="ResumenHija"/> (sin ids ni nombres: minimización L3).</summary>
    public string Resumen => HijaId is null ? ResumenRed : ResumenHija;

    /// <summary>
    /// <c>LoteConsolidados.alcanceRed</c> de un lote ya creado: <see cref="ResumenRed"/>, <see cref="ResumenHija"/> o
    /// <c>null</c> si no es de red (el <c>scope_tenant_id</c> del Super Admin no es vista de red).
    /// </summary>
    public static string? ResumenDe(ConsolidadoExportBatch lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        if (!lote.NetworkScope)
            return null;
        return lote.ScopeTenantId is null ? ResumenRed : ResumenHija;
    }
}

/// <summary>Lo que pidió el cuerpo: toda la red (<see cref="HijaId"/> <c>null</c>) o una hija concreta.</summary>
public sealed record AlcanceRedPedido(Guid? HijaId);

/// <summary>
/// HU #13417 (adenda v7, A7.2) — interpretación de <c>alcanceRed</c> y puertas de la vista de red para el lote. Las
/// puertas son las de <c>/network/**</c>, en el mismo orden y reutilizadas sin copiar:
/// <see cref="NetworkScopePolicy.Validate"/> (alcance de grupo) → <see cref="NetworkScopePolicy.ValidateRole"/> (rol
/// <c>AdminCompany</c> de los claims) → <see cref="NetworkScopePolicy.Narrow"/> (hija dentro del alcance) →
/// <see cref="NetworkDocumentsPolicy.ValidateKind"/> (documentos de red de una cabeza CONCESIÓN, P2 = a).
/// </summary>
/// <remarks>
/// Uso de ejemplo (endpoint):
/// <code>
/// if (!LoteAlcanceRedPolicy.TryInterpretar(body.AlcanceRed, body.Seleccion?.Filtro?.AlcanceRed, out var pedido)) return 400;
/// var (alcance, error) = await LoteAlcanceRedPolicy.EvaluarAsync(scopeDelMiddleware, rolesDelJwt, pedido!, switches, ct);
/// if (error is not null) return 403(error, LoteAlcanceRedPolicy.Mensaje(error));
/// </code>
/// </remarks>
public static class LoteAlcanceRedPolicy
{
    private const string Red = "red";

    /// <summary>
    /// Interpreta <c>alcanceRed</c> de la raíz del cuerpo y, durante la transición, el del filtro. Vacío o ausente =
    /// alcance propio (<paramref name="pedido"/> <c>null</c>). <c>false</c> (⇒ 400 <c>seleccion_invalida</c>) si un
    /// valor no es <c>red</c> ni un uuid distinto de cero, o si llegan los dos y difieren.
    /// </summary>
    public static bool TryInterpretar(string? raiz, string? filtro, out AlcanceRedPedido? pedido)
    {
        pedido = null;
        if (!TryUno(raiz, out var deRaiz) || !TryUno(filtro, out var deFiltro))
            return false;
        if (deRaiz is not null && deFiltro is not null && deRaiz != deFiltro)
            return false;

        pedido = deRaiz ?? deFiltro;
        return true;
    }

    /// <summary>
    /// Aplica las cuatro puertas al <paramref name="scope"/> del middleware. Devuelve el alcance efectivo, o
    /// <c>(null, null)</c> si el pedido es la propia cabeza (se normaliza al lote propio, como <c>Narrow</c>), o
    /// <c>(null, código)</c> con el 403 de la primera puerta que falla. El interruptor de documentos solo se lee para
    /// una cabeza CONCESIÓN (fail-closed: si no se puede leer, está apagado).
    /// </summary>
    public static async Task<(LoteAlcanceRed? Alcance, string? Error)> EvaluarAsync(
        TenantScope? scope,
        IEnumerable<string> roles,
        AlcanceRedPedido pedido,
        IHierarchySwitches switches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(switches);

        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);
        if (NetworkScopePolicy.ValidateRole(roles) is { } roleError)
            return (null, roleError);

        var (efectivo, narrowError) = NetworkScopePolicy.Narrow(scope!, pedido.HijaId);
        if (narrowError is not null)
            return (null, narrowError);
        if (pedido.HijaId is { } hija && hija == scope!.WriteTenantId)
            return (null, null);

        var concesionHabilitada = scope!.GroupKind == GroupKind.Concesion
            && await switches.IsNetworkDocumentsConcesionEnabledAsync(ct).ConfigureAwait(false);
        if (NetworkDocumentsPolicy.ValidateKind(scope, concesionHabilitada) is { } kindError)
            return (null, kindError);

        return (new LoteAlcanceRed(efectivo!, pedido.HijaId), null);
    }

    /// <summary>Texto del 403 para el usuario (sin códigos ni datos de la red).</summary>
    public static string Mensaje(string codigo) => codigo switch
    {
        NetworkScopePolicy.ScopeRequired =>
            "La descarga desde la vista de red solo está disponible para la compañía cabeza de una red.",
        NetworkScopePolicy.RoleRequired =>
            "Solo el administrador de la compañía cabeza puede descargar desde la vista de red.",
        NetworkScopePolicy.ChildOutOfScope => "La compañía elegida no pertenece a su red.",
        NetworkDocumentsPolicy.DocumentsDisabled =>
            "Su red no tiene habilitada la consulta de documentos de las compañías.",
        _ => "No tiene acceso a la vista de red.",
    };

    private static bool TryUno(string? valor, out AlcanceRedPedido? pedido)
    {
        pedido = null;
        if (string.IsNullOrWhiteSpace(valor))
            return true;

        var limpio = valor.Trim();
        if (string.Equals(limpio, Red, StringComparison.OrdinalIgnoreCase))
        {
            pedido = new AlcanceRedPedido(null);
            return true;
        }

        if (Guid.TryParse(limpio, out var hija) && hija != Guid.Empty)
        {
            pedido = new AlcanceRedPedido(hija);
            return true;
        }

        return false;
    }
}
