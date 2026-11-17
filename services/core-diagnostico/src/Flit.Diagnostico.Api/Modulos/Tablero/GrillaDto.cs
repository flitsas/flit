using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Respuesta de <c>GET /diagnosticos/{id}/grilla?desde=</c> (HU #13743, F04-H1, plan §6.1 fila 04). <c>version</c> es la
/// versión actual del tablero del lote: el cliente la manda como <c>desde</c> en el siguiente sondeo y recibe solo las
/// celdas con <c>version</c> mayor (con <c>desde=0</c>, todo). <c>vehiculos</c> trae solo los vehículos con alguna celda
/// cambiada y, de cada uno, solo esas celdas; <c>terminado</c> se calcula con TODAS sus celdas (R-7). El estado global,
/// las columnas y el avance van siempre. DTO de cliente: nunca lleva el origen de la celda, el usuario que la llenó a mano,
/// el payload ni documentos en claro (R-20; <c>GrillaTests</c> lo verifica por reflexión). Desde HU #14087 cada fila lleva el
/// documento declarado y el de cada propietario, solo con su tipo y ENMASCARADO en el servidor. <c>estimado_consultas</c> solo aparece con
/// <c>estimado_consultas=AL_FINALIZAR</c> en la copia de configuración del lote y el lote cerrado (R-11b). Desde el ajuste
/// 2026-11-11 (Punto 3) van SIEMPRE, también en el sondeo incremental, <c>total_pegadas</c> (placas del lote con alguna celda que el
/// reintento reencolaría, <see cref="Domain.Consultas.CriterioPegada"/>) y <c>vehiculos_pegados</c> (sus ids, en el orden del lote):
/// una placa puede quedar pegada sin que cambie ninguna celda (pasan los 10 min), así que el cliente no lo vería solo con las filas.
/// </summary>
public sealed record GrillaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("desde")] long Desde,
    [property: JsonPropertyName("columnas")] AlcanceDto Columnas,
    [property: JsonPropertyName("avance")] AvanceLoteDto Avance,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<FilaVehiculoDto> Vehiculos,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoVsConsumidoDto>? EstimadoConsultas,
    [property: JsonPropertyName("total_pegadas")] int TotalPegadas,
    [property: JsonPropertyName("vehiculos_pegados")] IReadOnlyList<Guid> VehiculosPegados)
{
    /// <summary>Rótulo del estado del lote (punto 5): «Sin lanzar», «En proceso», «Completado» o «Incompleto».</summary>
    [JsonPropertyName("estado_etiqueta")]
    public string EstadoEtiqueta => RotulosDiagnostico.DeEstadoLote(Estado);
}

/// <summary>Avance del lote: vehículos en total y cuántos ya terminaron (todas sus celdas en COMPLETADO o NA, R-7).</summary>
public sealed record AvanceLoteDto(
    [property: JsonPropertyName("vehiculos")] int Vehiculos,
    [property: JsonPropertyName("terminados")] int Terminados);

/// <summary>
/// Una fila de la grilla: el vehículo, sus celdas (solo las cambiadas desde <c>desde</c>) y sus documentos actuales (HU #14087):
/// el declarado por el cliente (null si no hay) y los propietarios del RUNT en su orden (vacío si aún no hay), enmascarados.
/// <c>pegada</c> (Punto 3, 2026-11-11): la placa tiene alguna celda que «Reintentar placa» reencolaría.
/// </summary>
public sealed record FilaVehiculoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("tipo_identificador")] string TipoIdentificador,
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("terminado")] bool Terminado,
    [property: JsonPropertyName("celdas")] IReadOnlyList<CeldaGrillaDto> Celdas,
    [property: JsonPropertyName("documento_declarado")] DocumentoDeclaradoDto? DocumentoDeclarado,
    [property: JsonPropertyName("propietarios")] IReadOnlyList<PropietarioGrillaDto> Propietarios,
    [property: JsonPropertyName("pegada")] bool Pegada,
    [property: JsonPropertyName("derivacion")] Derivacion.DerivacionFilaDto? Derivacion = null)
{
    /// <summary>Rótulo del tipo de identificador (punto 5): «Placa de carro», «Placa de motocarro», «VIN»…</summary>
    [JsonPropertyName("tipo_identificador_etiqueta")]
    public string TipoIdentificadorEtiqueta => RotulosDiagnostico.DeTipoIdentificador(TipoIdentificador);
}

/// <summary>
/// El documento que declaró el cliente para el vehículo (HU #14087; hoy el locatario de la captura masiva o Express): rol
/// (<c>PROPIETARIO</c> o <c>LOCATARIO</c>), tipo y número enmascarado (<c>****</c> + 4 últimos). Es el origen del DOCUMENTO,
/// que la compañía ya ve; el origen de la celda (API/MANUAL) sigue sin viajar (R-20).
/// </summary>
public sealed record DocumentoDeclaradoDto(
    [property: JsonPropertyName("rol")] string Rol,
    [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
    [property: JsonPropertyName("documento_enmascarado")] string DocumentoEnmascarado);

/// <summary>Un propietario que trajo el RUNT (HU #14087): tipo y número enmascarado; nunca el número en claro.</summary>
public sealed record PropietarioGrillaDto(
    [property: JsonPropertyName("tipo_documento")] string TipoDocumento,
    [property: JsonPropertyName("documento_enmascarado")] string DocumentoEnmascarado);

/// <summary>
/// Una celda del semáforo: fuente, estado, motivo de N/A, tipo de error y la fecha y hora del dato (D-10; en la reutilizada,
/// la del dato original). Sin origen, usuario manual, payload ni documentos (R-20).
/// </summary>
public sealed record CeldaGrillaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_na")] string? MotivoNa,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("dato_obtenido_en")] DateTimeOffset? DatoObtenidoEn,
    [property: JsonPropertyName("reutilizada")] bool Reutilizada,
    [property: JsonPropertyName("version")] long Version,
    /// <summary>Solo impuesto COMPLETADO con deuda confirmada. Ausente si es false. El semáforo no se pinta en verde.</summary>
    [property: JsonPropertyName("tiene_deuda")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool TieneDeuda = false)
{
    /// <summary>Rótulo del estado de la celda (punto 5): «Pendiente», «En curso», «Completado», «Error» o «No aplica».</summary>
    [JsonPropertyName("estado_etiqueta")]
    public string EstadoEtiqueta => RotulosDiagnostico.DeEstadoCelda(Estado);

    /// <summary>Texto del motivo de N/A (punto 5); null si la celda no tiene motivo.</summary>
    [JsonPropertyName("motivo_na_etiqueta")]
    public string? MotivoNaEtiqueta => RotulosDiagnostico.DeMotivoNa(MotivoNa);
}

/// <summary>
/// Estimado por fuente (calculador de HU #13747) frente a las consultas consumidas: intentos con <c>desde_cache=false</c>
/// (las reutilizadas del mismo día no cuentan, D-10) y sin los de llenado manual. En las fuentes de persona (HU #13796) las
/// estimadas son las personas registradas del rol y lo consumido, un intento por persona; <c>minimo=true</c> solo si algún
/// vehículo del lote no tiene personas registradas (lote anterior a D3 o RUNT sin respuesta) y ahí cuenta uno por vehículo.
/// </summary>
public sealed record EstimadoVsConsumidoDto(
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estimadas")] int Estimadas,
    [property: JsonPropertyName("consumidas")] int Consumidas,
    [property: JsonPropertyName("minimo")] bool Minimo);
