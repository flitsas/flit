using System.Text.Json.Serialization;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Página de <c>GET /diagnosticos</c> (HU #13845, F08-H1): lotes de la compañía del token visibles para el usuario, del más
/// reciente al más antiguo. <see cref="Total"/> cuenta todos los que cumplen los filtros (todas las páginas). Sin documentos,
/// sin configuración, sin alcance.
/// </summary>
public sealed record ListadoDiagnosticosDto(
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("diagnosticos")] IReadOnlyList<DiagnosticoListadoDto> Diagnosticos);

/// <summary>
/// Un lote del listado: <c>estado</c> es el código del lote (CREADO = «Sin lanzar» en la interfaz), <c>tipo_tramite</c> es null
/// en el modo SIMPLE y <c>usuario_creador.nombre</c> lo da Identidad («No disponible» si no responde).
/// </summary>
public sealed record DiagnosticoListadoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("tipo_tramite")] TipoTramiteListadoDto? TipoTramite,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("usuario_creador")] UsuarioCreadorDto UsuarioCreador);

/// <summary>Tipo de trámite del lote Con Análisis IA; si el código ya no está publicado en la etapa, el nombre es el código.</summary>
public sealed record TipoTramiteListadoDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre);

/// <summary>Creador del lote (claim <c>sub</c> al crearlo) con su nombre visible.</summary>
public sealed record UsuarioCreadorDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre);
