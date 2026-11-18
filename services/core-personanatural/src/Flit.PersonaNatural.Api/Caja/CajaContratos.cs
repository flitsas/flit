namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// Quién cobra y en qué organismo, tal como lo resolvió core-api en ESTA petición desde el JWT del usuario (el cliente final no lo
/// envía nunca). El código y el nombre del organismo los completa core-api; el portal solo usa el id.
/// </summary>
public sealed record Cajero(Guid UsuarioId, string Nombre, Guid OrganismoId, string OrganismoCodigo, string OrganismoNombre);

public sealed record CodigoNombre(string Codigo, string Nombre);

public sealed record LineaCaja(string Concepto, long ValorCop);

/// <summary>Lo único que la caja ve del trámite: tipo, organismo y desglose. Sin nombre, documento ni placa de las partes.</summary>
public sealed record TramiteParaCobrar(
    string Codigo, CodigoNombre TipoTramite, CodigoNombre Organismo, IReadOnlyList<LineaCaja> Lineas, long TotalCop, string VersionTarifa, DateTimeOffset VenceEn);

public sealed record CobroConfirmado(string Codigo, long ValorCop, string Medio, string? MedioDetalle, DateTimeOffset PagadoEn, DateTimeOffset? AnulableHasta = null);

/// <summary>Una fila de la lista del organismo (la usa la caja y el hub del OT por gRPC).</summary>
public sealed record CobroDelOrganismo(
    Guid CobroId, string Codigo, long ValorCop, string? MedioCaja, string? MedioDetalle, DateTimeOffset PagadoEn, string? PagadoPorNombre, string Estado,
    bool Anulable, string? MotivoNoAnulable, DateTimeOffset? AnuladoEn, string? AnuladoPorNombre, string? AnulacionMotivo, long RowVersion,
    DateTimeOffset? AnulableHasta = null);

public sealed record PaginaDeCobros(IReadOnlyList<CobroDelOrganismo> Items, int Total, long TotalDiaCop);

