using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Auditoría de versiones del dictamen de un vehículo (HU #14563 B7, D6/D7, ADR-0086 D-2). Cuelga del grupo <c>/backoffice</c>
/// (política de configuración, empresa explícita en <c>?empresa=</c>; la lectura queda en <c>lecturas_backoffice</c> como el
/// GET del dictamen) y exige la política de operación FLIT (<see cref="PoliticaBackoffice.NombreOperacion"/>, hallazgo M3: un
/// SuperAdmin de otra compañía recibe 403) y <c>diagnostico.auditoria.ver</c>. Devuelve la cadena completa en orden descendente: lo que vio la
/// compañía en cada versión, quién y por qué la ajustó (el nombre lo resuelve Identidad; no se guarda), los alertamientos
/// eliminados frente a la anterior, si un recálculo automático la reemplazó y si el ajuste se confirmó sobre un vehículo que ya tenía
/// trámite (AC23). Es la única salida de <c>ajuste_motivo</c>.
/// </summary>
internal static class VersionesDictamenEndpoints
{
    public const string Ruta = OverrideDictamenEndpoints.Ruta + "/versiones";

    public const string OrigenAutomatico = "AUTOMATICO";
    public const string OrigenAjusteFlit = "AJUSTE_FLIT";

    private static readonly JsonSerializerOptions Json = new();

    public static void Map(RouteGroupBuilder backoffice)
    {
        ArgumentNullException.ThrowIfNull(backoffice);
        // HU #14563 M3: trae ajuste_motivo (@pii:low) y el autor; solo la operación FLIT (un SuperAdmin de otra compañía, 403).
        backoffice.MapGet(Ruta, Listar).RequiereOperacionFlit().RequirePermiso(Permisos.AuditoriaVer).WithName("ListarVersionesDictamenBackoffice");
    }

    private static async Task<IResult> Listar(
        Guid id, Guid vid, HttpContext http, DiagnosticoDb db, UsuarioActual usuario, IResolutorNombresUsuario nombres, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        await OverrideDictamenEndpoints.ExigirLoteYVehiculoAsync(db, empresa, id, vid, ct).ConfigureAwait(false);
        var filas = await db.DeLaEmpresa<DictamenVehiculo>(empresa).Where(d => d.VehiculoId == vid)
            .OrderBy(d => d.Version).ToListAsync(ct).ConfigureAwait(false);
        if (filas.Count == 0)
            throw ConsultaDictamen.SinFila();

        // Los autores son usuarios de FLIT: el nombre se pide con la compañía de quien lee (la de FLIT). Si Identidad no responde,
        // el nombre queda null y la lectura sigue (mismo criterio que la grilla, HU #14563 AC11).
        var autores = filas.Where(f => f.UsuarioOverrideId is not null).Select(f => f.UsuarioOverrideId!.Value).Distinct().ToList();
        IReadOnlyDictionary<Guid, string> resueltos = new Dictionary<Guid, string>();
        if (autores.Count > 0 && usuario.EmpresaId is { } lector)
            resueltos = await nombres.ResolverAsync(lector, autores, ct).ConfigureAwait(false);

        return Results.Ok(new VersionesDictamenDto([.. Armar(filas, resueltos).OrderByDescending(v => v.Version)]));
    }

    /// <summary>
    /// Una entrada por versión, en el orden de <paramref name="filas"/> (ascendente por versión). <c>eliminados</c>: los
    /// alertamientos de la versión anterior que no siguen (por código y fuente). <c>superado_por_recalculo</c> (D7, derivado): la
    /// versión es un ajuste y la siguiente es automática.
    /// </summary>
    internal static IEnumerable<VersionDictamenDto> Armar(IReadOnlyList<DictamenVehiculo> filas, IReadOnlyDictionary<Guid, string> nombres)
    {
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(nombres);
        for (var i = 0; i < filas.Count; i++)
        {
            var fila = filas[i];
            var siguiente = i + 1 < filas.Count ? filas[i + 1] : null;
            var alertas = Alertas(fila.Alertamientos);
            var eliminados = i == 0 ? [] : Eliminados(Alertas(filas[i - 1].Alertamientos), alertas);
            UsuarioVersionDto? autor = fila.UsuarioOverrideId is { } u
                ? new UsuarioVersionDto(u, nombres.TryGetValue(u, out var nombre) && nombre != ResolutorNombresUsuario.NoDisponible ? nombre : null)
                : null;
            yield return new VersionDictamenDto(
                fila.Version,
                fila.Veredicto,
                fila.Vigente,
                alertas,
                eliminados,
                fila.AnalisisOverride ? OrigenAjusteFlit : OrigenAutomatico,
                autor,
                fila.AjusteMotivo,
                fila.AnalisisOverride && fila.OverrideEn is { } en ? en : fila.CreadoEn,
                fila.AjustadoDesdeVersion is not null && siguiente is { AnalisisOverride: false },
                fila.AjusteConTramite);
        }
    }

    private static List<AlertaVersionDto> Eliminados(List<AlertaVersionDto> anteriores, List<AlertaVersionDto> actuales)
    {
        var pendientes = actuales.ToList();
        var eliminados = new List<AlertaVersionDto>();
        foreach (var anterior in anteriores)
        {
            var i = pendientes.FindIndex(a => a.Codigo == anterior.Codigo && a.Fuente == anterior.Fuente);
            if (i >= 0)
                pendientes.RemoveAt(i);
            else
                eliminados.Add(anterior with { Ajuste = null });
        }

        return eliminados;
    }

    private static List<AlertaVersionDto> Alertas(string json) =>
        JsonSerializer.Deserialize<List<AlertaVersionDto>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json) ?? [];
}

/// <summary>Respuesta de <c>GET …/dictamen/versiones</c> (backoffice).</summary>
internal sealed record VersionesDictamenDto([property: JsonPropertyName("versiones")] IReadOnlyList<VersionDictamenDto> Versiones);

/// <summary>Una versión del dictamen con su auditoría. Solo backoffice: trae el motivo (<c>@pii:low</c>) y el autor.</summary>
internal sealed record VersionDictamenDto(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("veredicto")] string Veredicto,
    [property: JsonPropertyName("vigente")] bool Vigente,
    [property: JsonPropertyName("alertamientos")] IReadOnlyList<AlertaVersionDto> Alertamientos,
    [property: JsonPropertyName("eliminados")] IReadOnlyList<AlertaVersionDto> Eliminados,
    [property: JsonPropertyName("origen")] string Origen,
    [property: JsonPropertyName("usuario")] UsuarioVersionDto? Usuario,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("en")] DateTimeOffset En,
    [property: JsonPropertyName("superado_por_recalculo")] bool SuperadoPorRecalculo,
    [property: JsonPropertyName("ajuste_con_tramite")] bool AjusteConTramite);

/// <summary>Quien hizo el ajuste: el id guardado y el nombre de Identidad (null si no se pudo resolver).</summary>
internal sealed record UsuarioVersionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nombre")] string? Nombre);

/// <summary>Un alertamiento de una versión, con la marca <c>ajuste</c> (AGREGADO o EDITADO; null si no la tiene).</summary>
internal sealed record AlertaVersionDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("texto")] string Texto,
    [property: JsonPropertyName("efecto")] string Efecto,
    [property: JsonPropertyName("fuente")] string Fuente,
    [property: JsonPropertyName("ajuste")] string? Ajuste)
{
    /// <summary>Rótulo de la regla, el mismo del dictamen.</summary>
    [JsonPropertyName("etiqueta")]
    public string Etiqueta => RotulosDiagnostico.DeAlerta(Codigo, Fuente);
}
