using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Un alertamiento del dictamen, en el orden guardado. Sin documentos ni nombres. <c>etiqueta</c> y <c>fuente_etiqueta</c>
/// (punto 5, 2026-11-11) son los rótulos de <see cref="RotulosDiagnostico"/>: no se guardan, se calculan al responder.
/// </summary>
internal sealed record AlertaDictamenDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("efecto")] string Efecto,
    [property: JsonPropertyName("fuente")] string Fuente)
{
    /// <summary>Rótulo de la regla («SOAT no vigente»); sin rótulo, «Novedad sin clasificar».</summary>
    [JsonPropertyName("etiqueta")]
    public string Etiqueta => RotulosDiagnostico.DeAlerta(Codigo, Fuente);

    /// <summary>Nombre de la fuente («RUNT propietarios»); sin rótulo, «Novedad sin clasificar».</summary>
    [JsonPropertyName("fuente_etiqueta")]
    public string FuenteEtiqueta => RotulosDiagnostico.DeFuente(Fuente);
}

/// <summary>
/// Dictamen vigente que ve la compañía. No trae override, lease, intentos ni la empresa. Desde HU #14563 (D6, P1 = a) trae la
/// marca <c>ajustado_por_flit</c> y su fecha <c>ajustado_en</c>; nunca el autor, el motivo, la versión anterior ni la marca
/// <c>ajuste</c> de cada alertamiento.
/// </summary>
internal sealed record DictamenCompaniaDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("veredicto")] string Veredicto,
    [property: JsonPropertyName("estado_redaccion")] string EstadoRedaccion,
    [property: JsonPropertyName("alertamientos")] IReadOnlyList<AlertaDictamenDto> Alertamientos,
    [property: JsonPropertyName("catalogo_version")] int CatalogoVersion,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("ajustado_por_flit")] bool AjustadoPorFlit = false,
    [property: JsonPropertyName("ajustado_en")] DateTimeOffset? AjustadoEn = null);

/// <summary>Lo de la compañía más quién reemplazó el veredicto y cuándo. Sigue sin lease, intentos ni texto de cola.</summary>
internal sealed record DictamenBackofficeDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("veredicto")] string Veredicto,
    [property: JsonPropertyName("estado_redaccion")] string EstadoRedaccion,
    [property: JsonPropertyName("alertamientos")] IReadOnlyList<AlertaDictamenDto> Alertamientos,
    [property: JsonPropertyName("catalogo_version")] int CatalogoVersion,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("analisis_override")] bool AnalisisOverride,
    [property: JsonPropertyName("usuario_override_id")] Guid? UsuarioOverrideId,
    [property: JsonPropertyName("override_en")] DateTimeOffset? OverrideEn);

/// <summary>
/// Lectura de la vigente (corte 7). La compañía pasa por <see cref="AccesoLote"/>. El backoffice arma el mismo DTO con los
/// tres campos de override; la fila la lee el módulo Backoffice, con empresa explícita.
/// </summary>
internal static class ConsultaDictamen
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new();

    /// <summary>DTO de la compañía. No proyecta override, lease, intentos ni empresa.</summary>
    public static DictamenCompaniaDto Compania(DictamenVehiculo fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var ajustado = AjustadoPorFlit(fila);
        return new DictamenCompaniaDto(
            fila.Version, fila.Veredicto, fila.EstadoRedaccion, Alertas(fila.Alertamientos), fila.CatalogoVersion, fila.CreadoEn,
            ajustado, ajustado ? fila.OverrideEn : null);
    }

    /// <summary>
    /// P1 = a (2026-11-12): la marca «ajustado por FLIT» es <c>ajuste_motivo IS NOT NULL</c>, así que solo la llevan los ajustes
    /// posteriores al despliegue de B7. D7: un recálculo nace sin motivo y la marca desaparece.
    /// </summary>
    public static bool AjustadoPorFlit(DictamenVehiculo fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        return fila.AjusteMotivo is not null;
    }

    /// <summary>DTO de backoffice: el de la compañía y los tres campos de override.</summary>
    public static DictamenBackofficeDto Backoffice(DictamenVehiculo fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var comun = Compania(fila);
        return new DictamenBackofficeDto(
            comun.Version, comun.Veredicto, comun.EstadoRedaccion, comun.Alertamientos, comun.CatalogoVersion, comun.CreadoEn,
            fila.AnalisisOverride, fila.UsuarioOverrideId, fila.OverrideEn);
    }

    /// <summary>
    /// La vigente del vehículo si el lote se ve. Otra compañía, un lote ajeno o un vehículo de otro lote: 404
    /// <c>NO_ENCONTRADO</c>. Sin fila vigente: 404 <c>SIN_DICTAMEN</c>.
    /// </summary>
    public static async Task<DictamenCompaniaDto> LeerDeLaCompaniaAsync(
        DiagnosticoDb db, UsuarioActual usuario, Guid diagnosticoId, Guid vehiculoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(usuario);
        await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        var delLote = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .AnyAsync(v => v.Id == vehiculoId && v.DiagnosticoId == diagnosticoId, ct).ConfigureAwait(false);
        if (!delLote)
            throw ReglaException.NoEncontrado("El vehículo");

        var fila = await db.Set<DictamenVehiculo>().AsNoTracking()
            .SingleOrDefaultAsync(d => d.VehiculoId == vehiculoId && d.Vigente, ct).ConfigureAwait(false)
            ?? throw SinFila();
        return Compania(fila);
    }

    /// <summary>404 <c>SIN_DICTAMEN</c>: el vehículo se ve y no tiene dictamen vigente.</summary>
    internal static ReglaException SinFila() =>
        new(CodigosDictamen.SinDictamen, "Este vehículo no tiene un dictamen vigente.", StatusCodes.Status404NotFound);

    private static List<AlertaDictamenDto> Alertas(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<List<AlertaDictamenDto>>(json, Json)
        ?? throw new InvalidOperationException("Los alertamientos del dictamen no son un arreglo.");
}
