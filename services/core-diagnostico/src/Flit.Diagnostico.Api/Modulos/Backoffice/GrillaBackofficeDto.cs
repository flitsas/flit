using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence.Configuraciones;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Respuesta de <c>GET /backoffice/diagnosticos/{id}/grilla?empresa=&amp;desde=</c> (HU #13799, F06-C6): la grilla de la compañía
/// (<see cref="GrillaDto"/>, misma semántica de <c>desde</c>/<c>version</c>) más la empresa consultada y, en cada celda, el
/// origen del dato y el usuario del llenado manual. Sin payload ni documentos. El DTO de la compañía no cambia.
/// </summary>
internal sealed record GrillaBackofficeDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("desde")] long Desde,
    [property: JsonPropertyName("columnas")] AlcanceDto Columnas,
    [property: JsonPropertyName("avance")] AvanceLoteDto Avance,
    [property: JsonPropertyName("vehiculos")] IReadOnlyList<FilaVehiculoBackofficeDto> Vehiculos,
    [property: JsonPropertyName("estimado_consultas"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<EstimadoVsConsumidoDto>? EstimadoConsultas)
{
    public static GrillaBackofficeDto De(Guid empresa, GrillaLeida g)
    {
        ArgumentNullException.ThrowIfNull(g);
        return new(
            empresa, g.Cabecera.Id, g.Cabecera.Nombre, CodigosDeEnum.Codigo(g.Cabecera.Estado), g.Cabecera.VersionTablero, g.Desde,
            g.Columnas, g.Avance,
            [.. g.Vehiculos.Select(v => new FilaVehiculoBackofficeDto(
                v.Id, v.Orden, v.Identificador, v.TipoIdentificador, v.Placa, v.Vin, v.Terminado, [.. v.Celdas.Select(CeldaGrillaBackofficeDto.De)]))],
            g.Estimado);
    }
}

/// <summary>Una fila de la grilla del backoffice: igual a <see cref="FilaVehiculoDto"/> con celdas del backoffice.</summary>
internal sealed record FilaVehiculoBackofficeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("orden")] int Orden,
    [property: JsonPropertyName("identificador")] string Identificador,
    [property: JsonPropertyName("tipo_identificador")] string TipoIdentificador,
    [property: JsonPropertyName("placa")] string? Placa,
    [property: JsonPropertyName("vin")] string? Vin,
    [property: JsonPropertyName("terminado")] bool Terminado,
    [property: JsonPropertyName("celdas")] IReadOnlyList<CeldaGrillaBackofficeDto> Celdas);

/// <summary>
/// Una celda para el backoffice: las claves de <see cref="CeldaGrillaDto"/> más <c>origen</c> (<c>API</c> consulta a la fuente,
/// también la reutilizada del mismo día, que se distingue con <c>reutilizada=true</c>; <c>MANUAL</c> llenado del backoffice;
/// null mientras no hay dato) y <c>usuario_manual_id</c> (solo con <c>MANUAL</c>; si no, null).
/// </summary>
internal sealed record CeldaGrillaBackofficeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo_na")] string? MotivoNa,
    [property: JsonPropertyName("tipo_error")] string? TipoError,
    [property: JsonPropertyName("dato_obtenido_en")] DateTimeOffset? DatoObtenidoEn,
    [property: JsonPropertyName("reutilizada")] bool Reutilizada,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("origen")] string? Origen,
    [property: JsonPropertyName("usuario_manual_id")] Guid? UsuarioManualId)
{
    public static CeldaGrillaBackofficeDto De(CeldaLeida c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(
            c.Id, CodigosDeEnum.Codigo(c.Fuente), CodigosDeEnum.Codigo(c.Estado), c.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : null,
            c.TipoError, c.DatoObtenidoEn, c.Reutilizada, c.Version,
            c.Origen is { } o ? CodigosDeEnum.Codigo(o) : null, c.UsuarioManualId);
    }
}

/// <summary>Un lote en la lista del backoffice (HU #13799): sin datos de vehículos ni de personas.</summary>
internal sealed record DiagnosticoBackofficeDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("vehiculos")] int Vehiculos,
    [property: JsonPropertyName("celdas_en_error")] int CeldasEnError);

/// <summary>Página de lotes de una compañía para el backoffice (HU #13799).</summary>
internal sealed record ListaDiagnosticosBackofficeDto(
    [property: JsonPropertyName("empresa")] Guid Empresa,
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("diagnosticos")] IReadOnlyList<DiagnosticoBackofficeDto> Diagnosticos);
