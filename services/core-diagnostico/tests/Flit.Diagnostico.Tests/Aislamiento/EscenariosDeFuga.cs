using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// Lo que una empresa sembró para un escenario: la URL a leer y los identificadores que su lectura devuelve. Lo que no debe
/// cruzar a la otra empresa son <see cref="Exclusivos"/> si se dan (un catálogo global devuelve lo mismo a todas: lo
/// exclusivo de la empresa es su id), o los mismos <see cref="Identificadores"/>.
/// </summary>
public sealed record Sembrado(string Url, IReadOnlyList<string> Identificadores, IReadOnlyList<string>? Exclusivos = null)
{
    public IReadOnlyList<string> QueNoCruzan => Exclusivos ?? Identificadores;
}

/// <summary>Cómo sembrar datos de una empresa para un GET y qué identificadores no deben cruzar a la otra.</summary>
public sealed record EscenarioDeFuga(string Ruta, Func<EmpresaDePrueba, CancellationToken, Task<Sembrado>> Sembrar)
{
    /// <summary>
    /// Forma corta: siembra una entidad de la empresa (su <c>TenantId</c> lo pone el guardado) y la URL es <see cref="Ruta"/>
    /// con <c>{id}</c> reemplazado por <paramref name="id"/> de esa entidad. Identificadores: ese id y el de la empresa.
    /// </summary>
    public static EscenarioDeFuga Con<T>(string ruta, Func<EmpresaDePrueba, T> crear, Func<T, Guid> id)
        where T : class, IEntidadDeEmpresa
    {
        ArgumentNullException.ThrowIfNull(crear);
        ArgumentNullException.ThrowIfNull(id);
        return new EscenarioDeFuga(ruta, async (empresa, ct) =>
        {
            var entidad = crear(empresa);
            await using var db = empresa.Db();
            db.Set<T>().Add(entidad);
            await db.SaveChangesAsync(ct);
            var propio = id(entidad).ToString();
            return new Sembrado(ruta.Replace("{id}", propio, StringComparison.Ordinal), [propio, empresa.Id.ToString()]);
        });
    }
}

/// <summary>
/// Punto de registro de la prueba de fuga (HU #13722). Cada GET del servicio fuera de <c>/health*</c> y <c>/backoffice/*</c>
/// suma aquí una línea en <see cref="Todos"/> con su patrón de ruta exacto (el de <c>RoutePattern.RawText</c>):
/// <code>
/// EscenarioDeFuga.Con(Program.Prefijo + "/diagnosticos/{id}", e => new Diagnostico { Nombre = "Lote" }, d => d.Id),
/// </code>
/// Sin esa línea, <c>Todo_endpoint_GET_tiene_escenario_de_fuga</c> falla y nombra el endpoint. Si la siembra no cabe en
/// una entidad, use el constructor con su propia función <see cref="EscenarioDeFuga.Sembrar"/>.
/// </summary>
public static class EscenariosDeFuga
{
    /// <summary>Sonda del arnés: un GET de prueba que lee por el contexto de datos, para que la suite nunca quede vacía.</summary>
    public const string RutaSonda = Program.Prefijo + "/__sonda-fuga/lecturas-backoffice";

    /// <summary>
    /// Sesión (HU #13724): no lee la base, todo sale del token. La URL de cada empresa lleva <c>?empresa=</c> con su id,
    /// así que con el token de A la URL de B pide la compañía B por parámetro y no debe devolver nada de ella.
    /// </summary>
    public const string RutaSesion = Program.Prefijo + "/sesion";

    /// <summary>
    /// Configuración de la compañía (HU #13741): cada empresa siembra su fila con un autor operativo propio, que la lectura
    /// devuelve junto con el id de la empresa; con el token de A no debe aparecer nada de B.
    /// </summary>
    public const string RutaConfiguracion = Program.Prefijo + ConfiguracionEndpoints.Ruta;

    /// <summary>
    /// Tipos de trámite (HU #13742): catálogo global de Trámites, igual para toda compañía. A debe recibir el catálogo y
    /// ninguna respuesta lleva el id de B (ni de ninguna compañía).
    /// </summary>
    public const string RutaTiposTramite = Program.Prefijo + TiposTramiteEndpoints.Ruta;

    public static IReadOnlyList<EscenarioDeFuga> Todos { get; } =
    [
        EscenarioDeFuga.Con(RutaSonda, _ => new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/sonda" }, l => l.Id),
        new EscenarioDeFuga(RutaSesion, (empresa, _) =>
            Task.FromResult(new Sembrado($"{RutaSesion}?empresa={empresa.Id}", [empresa.Id.ToString()]))),
        EscenarioDeFuga.Con(RutaConfiguracion, _ => new ConfiguracionEmpresa { OperativaActualizadaPor = Guid.NewGuid() }, c => c.OperativaActualizadaPor!.Value),
        new EscenarioDeFuga(RutaTiposTramite, (empresa, _) =>
            Task.FromResult(new Sembrado(RutaTiposTramite, ["TRASPASO_STANDARD"], Exclusivos: [empresa.Id.ToString()]))),
    ];

    public static EscenarioDeFuga Buscar(string ruta) => Todos.Single(e => e.Ruta == ruta);

    /// <summary>Mapea las sondas del arnés en la API de prueba (no existen en el servicio real).</summary>
    public static void MapearSondas(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet(RutaSonda, async (DiagnosticoDb db, CancellationToken ct) =>
                Results.Ok(await db.Set<LecturaBackoffice>().AsNoTracking()
                    .OrderBy(l => l.Fecha).Take(100)
                    .Select(l => new { l.Id, l.TenantId, l.EmpresaConsultada })
                    .ToListAsync(ct)))
            .RequireAuthorization();
    }

    /// <summary>Patrones de los GET registrados que deben tener escenario y no lo tienen.</summary>
    public static IReadOnlyList<string> EndpointsSinEscenario(IEnumerable<EndpointDataSource> fuentes)
    {
        ArgumentNullException.ThrowIfNull(fuentes);
        var conEscenario = Todos.Select(e => e.Ruta).ToHashSet(StringComparer.Ordinal);
        return fuentes.SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) == true)
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(r => !Exento(r) && !conEscenario.Contains(r))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static bool Exento(string ruta) =>
        ruta == "/health" || ruta.StartsWith("/health/", StringComparison.Ordinal)
        || ruta.StartsWith(Program.Prefijo + "/backoffice/", StringComparison.Ordinal);
}
