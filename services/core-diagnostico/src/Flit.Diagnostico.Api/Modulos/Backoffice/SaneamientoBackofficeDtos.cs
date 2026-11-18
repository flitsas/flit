using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Persistence.Configuraciones;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Saneamiento que ve FLIT (plan §6.2 y §6.3; HU #14576 AC27; SP-15, D-22, D-60, D-63). Tipo separado del de la compañía: el historial
/// trae todos los eventos con <c>usuario_id</c> y los estados de cada transición, y no lleva <c>solicitado_por_mi</c>. Nunca trae la
/// referencia del almacén ni el sha de la evidencia. Lleva el contexto del encabezado de la gestión (lote, vehículo, placa y veredicto
/// vigente). <c>version</c> es el <c>xmin</c> del saneamiento.
/// </summary>
internal sealed record SaneamientoBackofficeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("version")] uint Version,
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("diagnostico_nombre")] string DiagnosticoNombre,
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("veredicto")] string? Veredicto,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("solicitado_en")] DateTimeOffset SolicitadoEn,
    [property: JsonPropertyName("tomado_en")] DateTimeOffset? TomadoEn,
    [property: JsonPropertyName("cerrado_en")] DateTimeOffset? CerradoEn,
    [property: JsonPropertyName("motivo_cierre")] string? MotivoCierre,
    [property: JsonPropertyName("motivo_anulacion")] string? MotivoAnulacion,
    [property: JsonPropertyName("observacion_flit")] string? ObservacionFlit,
    [property: JsonPropertyName("renglones")] IReadOnlyList<RenglonClienteDto> Renglones,
    [property: JsonPropertyName("historial")] IReadOnlyList<EventoBackofficeDto> Historial);

/// <summary>Un evento del historial para FLIT: el de la compañía más quién actuó y los estados de la transición (null en las ediciones).</summary>
internal sealed record EventoBackofficeDto(
    [property: JsonPropertyName("evento")] string Evento,
    [property: JsonPropertyName("ocurrido_en")] DateTimeOffset OcurridoEn,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("fuente")] string? Fuente,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("usuario_id")] Guid UsuarioId,
    [property: JsonPropertyName("estado_anterior")] string? EstadoAnterior,
    [property: JsonPropertyName("estado_nuevo")] string? EstadoNuevo);

/// <summary>
/// Una fila de la bandeja entre compañías (plan §6.2): sin datos personales. AP-1 (UA-10, D-131): <c>vin</c> e <c>identificador</c> (el
/// valor cargado), siempre presentes y nullables como en <c>FilaPlacaDto</c>, para nombrar una fila sin placa.
/// </summary>
internal sealed record FilaBandejaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("diagnostico_id")] Guid DiagnosticoId,
    [property: JsonPropertyName("diagnostico_nombre")] string DiagnosticoNombre,
    [property: JsonPropertyName("vehiculo_id")] Guid VehiculoId,
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("veredicto")] string? Veredicto,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("solicitado_en")] DateTimeOffset SolicitadoEn,
    [property: JsonPropertyName("tomado_en")] DateTimeOffset? TomadoEn,
    [property: JsonPropertyName("cerrado_en")] DateTimeOffset? CerradoEn,
    [property: JsonPropertyName("renglones_total")] int RenglonesTotal,
    [property: JsonPropertyName("renglones_pendientes")] int RenglonesPendientes,
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("identificador")] string? Identificador);

/// <summary>Página de la bandeja (D-19: la lista se llama <c>saneamientos</c>).</summary>
internal sealed record ListaBandejaDto(
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("saneamientos")] IReadOnlyList<FilaBandejaDto> Saneamientos);

/// <summary>Insignia del menú (S19-bis): solicitudes sin tomar de todas las compañías.</summary>
internal sealed record ResumenBandejaDto([property: JsonPropertyName("solicitados")] int Solicitados);

/// <summary>Respuesta de «tomar varios»: cuántos pasaron a EN_GESTION y los omitidos en el orden pedido.</summary>
internal sealed record ResultadoTomarDto(
    [property: JsonPropertyName("tomados")] int Tomados,
    [property: JsonPropertyName("omitidos")] IReadOnlyList<OmitidoTomarDto> Omitidos);

/// <summary>Un ítem no tomado: <c>NO_ENCONTRADO</c> (también si el par empresa/id no coincide) o <c>TRANSICION_INVALIDA</c>.</summary>
internal sealed record OmitidoTomarDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("motivo")] string Motivo);

/// <summary>El saneamiento no anulado en una fila de la grilla del backoffice (AC35): estado e id, sin elegibilidad; null si no hay.</summary>
internal sealed record SaneamientoFilaBackofficeDto(
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("id")] Guid Id);

/// <summary>Proyecciones del saneamiento compartidas por las rutas del backoffice.</summary>
internal static class ProyeccionSaneamiento
{
    /// <summary>Un renglón no quitado, con la misma forma que ve la compañía (D-22: sin referencia ni sha).</summary>
    public static RenglonClienteDto Renglon(RenglonSaneamiento r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new RenglonClienteDto(
            r.Id, r.Xmin, CodigosDeEnum.Codigo(r.Fuente), r.Hallazgo is { } h ? CodigosDeEnum.Codigo(h) : null, r.Descripcion, r.Automatico,
            CodigosDeEnum.Codigo(r.Resultado), r.Nota,
            r.EvidenciaNombre is { } nombre && r.EvidenciaTamano is { } tamano && r.EvidenciaSubidaEn is { } subida
                ? new EvidenciaClienteDto(nombre, tamano, subida)
                : null);
    }

    public static EventoBackofficeDto Evento(EventoSaneamiento e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return new EventoBackofficeDto(
            CodigosDeEnum.Codigo(e.Evento), e.OcurridoEn, e.EsFlit ? "FLIT" : "COMPANIA", e.Fuente is { } f ? CodigosDeEnum.Codigo(f) : null, e.Motivo,
            e.UsuarioId, e.EstadoAnterior is { } a ? CodigosDeEnum.Codigo(a) : null, e.EstadoNuevo is { } n ? CodigosDeEnum.Codigo(n) : null);
    }
}
