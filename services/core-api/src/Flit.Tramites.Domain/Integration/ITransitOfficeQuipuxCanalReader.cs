namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Banderas Quipux de la secretaría DESTINO (<c>catalogs.transit_offices</c>) para decidir si el
/// submit deja el trámite en <c>preparado</c> (Bug #13912). No confundir con
/// <c>operation_mode = quipux</c> del perfil OT-cliente.
/// </summary>
public sealed record TransitOfficeQuipuxCanal(
    string? DivipoCode,
    bool QuipuxRegistration,
    bool QuipuxTransfer,
    bool QuipuxOther);

/// <summary>Puerto de lectura de DIVIPO + banderas de familia Quipux por OT.</summary>
public interface ITransitOfficeQuipuxCanalReader
{
    /// <summary>Devuelve el canal Quipux del OT, o <c>null</c> si no existe.</summary>
    Task<TransitOfficeQuipuxCanal?> GetAsync(Guid transitOfficeId, CancellationToken cancellationToken = default);
}

/// <summary>Default seguro para tests: ningún OT integrado.</summary>
public sealed class NullTransitOfficeQuipuxCanalReader : ITransitOfficeQuipuxCanalReader
{
    public static NullTransitOfficeQuipuxCanalReader Instance { get; } = new();

    public Task<TransitOfficeQuipuxCanal?> GetAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<TransitOfficeQuipuxCanal?>(null);
}
