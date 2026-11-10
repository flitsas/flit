using Flit.PersonaNatural.Api.Cotizaciones;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Quién cobra y en qué organismo, tal como lo resolvió core-api en ESTA petición (<c>ResolverUsuarioCaja</c>, sin caché). Es la
/// única fuente del organismo: el cliente no lo envía nunca.
/// </summary>
public sealed record Cajero(Guid UsuarioId, string Nombre, Guid OrganismoId, string OrganismoCodigo, string OrganismoNombre);

public sealed record CodigoNombre(string Codigo, string Nombre);

public sealed record LineaCaja(string Concepto, long ValorCop);

/// <summary>Lo único que la caja ve del trámite: tipo, organismo y desglose. Sin nombre, documento ni placa de las partes.</summary>
public sealed record TramiteParaCobrar(
    string Codigo, CodigoNombre TipoTramite, CodigoNombre Organismo, IReadOnlyList<LineaCaja> Lineas, long TotalCop, string VersionTarifa, DateTimeOffset VenceEn);

public sealed record CobroConfirmado(string Codigo, long ValorCop, string Medio, string? MedioDetalle, DateTimeOffset PagadoEn);

public sealed record ItemCobroDelDia(string Codigo, long ValorCop, string Medio, string? MedioDetalle, string Hora, DateTimeOffset PagadoEn, string Usuario, string Estado);

public sealed record CobrosDelDia(string Fecha, long TotalDiaCop, IReadOnlyList<ItemCobroDelDia> Items, int Total, int Pagina, int Tamano);

/// <summary>Una fila de la lista del organismo (la usa la caja y el hub del OT por gRPC).</summary>
public sealed record CobroDelOrganismo(
    Guid CobroId, string Codigo, long ValorCop, string? MedioCaja, string? MedioDetalle, DateTimeOffset PagadoEn, string? PagadoPorNombre, string Estado,
    bool Anulable, string? MotivoNoAnulable, DateTimeOffset? AnuladoEn, string? AnuladoPorNombre, string? AnulacionMotivo, long RowVersion);

public sealed record PaginaDeCobros(IReadOnlyList<CobroDelOrganismo> Items, int Total, long TotalDiaCop);

/// <summary>Usuario de caja que cobra: qué dice core-api de él.</summary>
public interface IResolverCajero
{
    /// <summary>El cajero activo y su organismo, o <c>null</c> si no es un usuario <c>ot_caja</c> activo con producto encendido.</summary>
    Task<Cajero?> ResolverAsync(Guid tenantId, Guid usuarioId, CancellationToken ct);
}
