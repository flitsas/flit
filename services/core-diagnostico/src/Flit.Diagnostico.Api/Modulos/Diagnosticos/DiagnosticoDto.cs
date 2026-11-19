using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Diagnosticos;

namespace Flit.Diagnostico.Api.Modulos.Diagnosticos;

/// <summary>
/// Cuerpo de <c>POST /diagnosticos</c> y <c>PUT /diagnosticos/{id}</c> (HU #13750): nombre, modo (<c>SIMPLE</c> o
/// <c>CON_ANALISIS</c>), tipo de trámite (solo Con Análisis IA) y el alcance en sus tres grupos, con los códigos de la base.
/// Los valores llegan como texto y los valida <see cref="ServicioCabecera"/> (422 con el campo).
/// </summary>
public sealed record CabeceraEntradaDto(
    [property: JsonPropertyName("nombre")] string? Nombre,
    [property: JsonPropertyName("modo")] string? Modo,
    [property: JsonPropertyName("tipo_tramite_codigo")] string? TipoTramiteCodigo,
    [property: JsonPropertyName("alcance")] AlcanceDto? Alcance);

/// <summary>Alcance en sus tres grupos (R-24): por vehículo, por propietario y por locatario.</summary>
public sealed record AlcanceDto(
    [property: JsonPropertyName("por_vehiculo")] IReadOnlyList<string>? PorVehiculo,
    [property: JsonPropertyName("por_propietario")] IReadOnlyList<string>? PorPropietario,
    [property: JsonPropertyName("por_locatario")] IReadOnlyList<string>? PorLocatario)
{
    public static AlcanceDto De(Alcance alcance)
    {
        ArgumentNullException.ThrowIfNull(alcance);
        return new(Codigos.De(alcance.PorVehiculo), Codigos.De(alcance.PorPropietario), Codigos.De(alcance.PorLocatario));
    }
}

/// <summary>
/// Cabecera del diagnóstico que devuelven la creación (201) y la edición (200), HU #13750. <c>configuracion</c> es la copia
/// guardada al crear (<see cref="CopiaConfiguracion"/>, sin <c>redaccion_ia_suspendida</c>).
/// </summary>
public sealed record DiagnosticoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("modo")] string Modo,
    [property: JsonPropertyName("tipo_tramite_codigo")] string? TipoTramiteCodigo,
    [property: JsonPropertyName("es_express")] bool EsExpress,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("alcance")] AlcanceDto Alcance,
    [property: JsonPropertyName("configuracion")] JsonElement Configuracion,
    [property: JsonPropertyName("usuario_creador_id")] Guid UsuarioCreadorId,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn)
{
    public static DiagnosticoDto De(CabeceraDiagnostico c)
    {
        ArgumentNullException.ThrowIfNull(c);
        using var configuracion = JsonDocument.Parse(c.Configuracion);
        return new(
            c.Id, c.Nombre, CodigosDeEnum.Codigo(c.Modo), c.TipoTramiteCodigo, c.EsExpress, CodigosDeEnum.Codigo(c.Estado),
            AlcanceDto.De(Domain.Diagnosticos.Alcance.Agrupar(c.Alcance)), configuracion.RootElement.Clone(), c.UsuarioCreadorId, c.CreadoEn);
    }
}
