namespace Flit.PersonaNatural.Api.Radicados;

/// <summary>Lo mínimo de la solicitud que hace falta para resolver su estado en FLIT 2 (sin datos personales).</summary>
public sealed record SolicitudParaEstado(
    Guid Id, Guid? ProcedureInstanceId, string? ReferenceNumber, string? EstadoTramite, bool SubsanacionActiva, string? SubsanacionMotivo);

/// <summary>Estado del trámite en FLIT 2 y, si fue rechazado, si hay subsanación activa y su motivo.</summary>
public sealed record EstadoTramiteExterno(string? EstadoTramite, bool SubsanacionActiva, string? SubsanacionMotivo);

/// <summary>
/// Costura de la fuente del estado (HU #14042). Hoy la única implementación es el espejo local (<c>solicitud_tramite</c>,
/// que mantendrá la HU D5 con los eventos de FLIT 2). La consulta gRPC <c>PersonaNaturalService.ObtenerEstadoTramite</c> irá
/// detrás de esta misma interfaz para las solicitudes que ya salieron a FLIT 2 y aún no tienen estado espejado.
/// </summary>
public interface IEstadoTramiteFuente
{
    Task<EstadoTramiteExterno> ObtenerAsync(SolicitudParaEstado solicitud, CancellationToken ct);
}

/// <summary>Lee el estado que el BFF ya espejó en su propia base.</summary>
public sealed class EstadoTramiteEspejo : IEstadoTramiteFuente
{
    public Task<EstadoTramiteExterno> ObtenerAsync(SolicitudParaEstado solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return Task.FromResult(new EstadoTramiteExterno(solicitud.EstadoTramite, solicitud.SubsanacionActiva, solicitud.SubsanacionMotivo));
    }
}
