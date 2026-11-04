namespace Flit.Admin.Domain.Plataforma.PersonaNatural;

/// <summary>
/// HU #14071 (Feature J, Épica #14021) — conceptos de la tarifa del portal de Persona Natural. Los mismos del CHECK de
/// <c>admin.persona_natural_tariffs</c> y de <c>ConceptosTarifa</c> (Tramites.Domain): Admin no referencia Tramites, y una
/// prueba de Admin.Tests garantiza que las dos listas no se separen.
/// </summary>
public static class ConceptosTarifaPn
{
    public const string DerechosTransito = "DERECHOS_TRANSITO";
    public const string ServicioFlit = "SERVICIO_FLIT";

    public static readonly IReadOnlyList<string> Todos =
    [
        DerechosTransito, ServicioFlit, "CAMBIO_COLOR", "CONVERSION_COMBUSTIBLE", "CAMBIO_CARROCERIA", "ENVIO_DOCUMENTO",
    ];

    /// <summary>Los dos renglones base siempre son «de un tipo»; solo los adicionales pueden valer para cualquiera.</summary>
    public static bool ExigeTipo(string concepto) => concepto is DerechosTransito or ServicioFlit;
}

/// <summary>Estado de una versión de tarifa a una fecha: vigente, futura (aún no inicia) o cerrada (historial).</summary>
public static class EstadosTarifaPn
{
    public const string Vigente = "vigente";
    public const string Futura = "futura";
    public const string Cerrada = "cerrada";
}

/// <summary>Una versión de tarifa con los nombres legibles del tipo y el organismo. <c>null</c> en tipo u organismo = valor general.</summary>
public sealed record TarifaPnView(
    Guid Id,
    string Concepto,
    Guid? TipoTramiteId,
    string? TipoTramiteCodigo,
    string? TipoTramiteNombre,
    Guid? OrganismoId,
    string? OrganismoCodigo,
    string? OrganismoNombre,
    long ValorCop,
    DateOnly VigenteDesde,
    DateOnly? VigenteHasta,
    string Estado,
    DateTimeOffset CreadaEn,
    Guid? CreadaPor,
    DateTimeOffset? ActualizadaEn,
    Guid? ActualizadaPor);

/// <summary>Filtros del listado. <c>SoloVigentes</c> deja las versiones en vigor en <c>Fecha</c>; <c>Fecha</c> nula = hoy en Colombia.</summary>
public sealed record TarifaPnFiltro(bool SoloVigentes, Guid? TipoTramiteId, Guid? OrganismoId, string? Concepto, DateOnly? Fecha);

/// <summary>Un valor de la versión que se registra.</summary>
public sealed record TarifaPnValor(string Concepto, long ValorCop);

/// <summary>Resultado de registrar versiones: lo que quedó (nuevas) y lo que estaba abierto antes (para la auditoría).</summary>
public sealed record TarifaPnVersionadaResult(IReadOnlyList<TarifaPnView> Nuevas, IReadOnlyList<TarifaPnView> Anteriores);

/// <summary>El ámbito ya tiene una versión que se cruza con la vigencia pedida (misma fecha de inicio o una posterior).</summary>
public sealed class TarifaPnSolapadaException(string concepto) : Exception($"Ya existe una tarifa para {concepto} en esas fechas.")
{
    public string Concepto { get; } = concepto;
}

/// <summary>
/// Persistencia de las tarifas del portal (<c>admin.persona_natural_tariffs</c>). Tabla global sin tenant. Las versiones no se
/// editan ni se borran: el único cambio de valor es una versión nueva (la base lo impone con un trigger).
/// </summary>
public interface IPersonaNaturalTarifaRepository
{
    /// <summary>Hoy en Colombia (la vigencia se mide en días).</summary>
    DateOnly HoyColombia();

    Task<IReadOnlyList<TarifaPnView>> ListarAsync(TarifaPnFiltro filtro, CancellationToken ct = default);

    Task<TarifaPnView?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Todas las versiones del mismo ámbito (concepto, tipo y organismo) de la versión indicada, de la más reciente a la más antigua.</summary>
    Task<IReadOnlyList<TarifaPnView>?> HistorialAsync(Guid id, CancellationToken ct = default);

    Task<bool> TipoExisteAsync(Guid tipoId, CancellationToken ct = default);

    Task<bool> OrganismoExisteAsync(Guid organismoId, CancellationToken ct = default);

    /// <summary>
    /// Registra una versión nueva por cada valor, en UNA transacción: cierra la abierta anterior el día anterior al inicio y
    /// abre la nueva. Lanza <see cref="TarifaPnSolapadaException"/> (y no deja nada) si alguna se cruza con otra.
    /// </summary>
    Task<TarifaPnVersionadaResult> VersionarAsync(
        Guid? tipoId, Guid? organismoId, IReadOnlyList<TarifaPnValor> valores, DateOnly vigenteDesde, Guid actor, CancellationToken ct = default);

    /// <summary>
    /// «Tarifa vigente» de un tipo en un organismo a una fecha: por cada concepto, el valor que se aplicaría (con la misma
    /// precedencia que la cotización: tipo+organismo, tipo, organismo, general).
    /// </summary>
    Task<IReadOnlyList<TarifaPnView>> VigentesDelTipoEnOrganismoAsync(Guid tipoId, Guid organismoId, DateOnly fecha, CancellationToken ct = default);
}
