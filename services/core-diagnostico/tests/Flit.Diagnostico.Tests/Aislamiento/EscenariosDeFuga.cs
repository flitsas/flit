using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Carga;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
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

/// <summary>
/// Cómo sembrar datos de una empresa para un GET y qué identificadores no deben cruzar a la otra. <see cref="LeerTexto"/> (HU #13848)
/// lee el cuerpo de una respuesta binaria como texto (el texto extraído del PDF, las celdas del Excel); sin él, el cuerpo tal cual.
/// </summary>
public sealed record EscenarioDeFuga(
    string Ruta,
    Func<EmpresaDePrueba, CancellationToken, Task<Sembrado>> Sembrar,
    Func<HttpResponseMessage, CancellationToken, Task<string>>? LeerTexto = null)
{
    /// <summary>El cuerpo de <paramref name="respuesta"/> como texto, con <see cref="LeerTexto"/> si el escenario lo trae.</summary>
    public Task<string> TextoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        return LeerTexto is { } leer ? leer(respuesta, ct) : respuesta.Content.ReadAsStringAsync(ct);
    }

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

    /// <summary>
    /// Plantilla de carga masiva (HU #13749): un .xlsx fijo, igual para toda compañía. A debe recibir el libro (las rutas
    /// de sus partes van sin comprimir en el zip) y ninguna respuesta lleva el id de B (ni de ninguna compañía).
    /// </summary>
    public const string RutaPlantilla = Program.Prefijo + CargaEndpoints.RutaPlantilla;

    /// <summary>
    /// Grilla del tablero (HU #13743): cada empresa siembra un lote con un vehículo y su celda; la grilla devuelve el id del
    /// lote y el del vehículo, y con el token de A no debe aparecer nada de B (la de B responde 404).
    /// </summary>
    public const string RutaGrilla = Program.Prefijo + EndpointsTablero.RutaGrilla;

    /// <summary>Historial de estados del lote (HU #13743): devuelve el id del lote; el de B responde 404 al token de A.</summary>
    public const string RutaHistorial = Program.Prefijo + EndpointsTablero.RutaHistorial;

    /// <summary>
    /// Detalle de una celda (HU #13798): cada empresa siembra un lote con la celda RUNT completada; el detalle devuelve el id
    /// de la celda y el del vehículo, y con el token de A la URL de B responde 404 sin nada de B.
    /// </summary>
    public const string RutaDetalleCelda = Program.Prefijo + EndpointsTablero.RutaDetalleCelda;

    /// <summary>
    /// Listado de diagnósticos de la compañía (HU #13845): cada empresa siembra un lote; el listado de A trae el id de su lote
    /// y nunca el de B (la URL es la misma para las dos: la compañía sale del token).
    /// </summary>
    public const string RutaListado = Program.Prefijo + global::Flit.Diagnostico.Api.Modulos.Diagnosticos.DiagnosticosEndpoints.Ruta;

    /// <summary>
    /// PDF corporativo de un vehículo (HU #13848): cada empresa siembra un lote con un vehículo terminado; el texto extraído del PDF
    /// (<see cref="Reportes.PdfDePrueba.TextoAsync"/>) trae el ID del diagnóstico, y con el token de A el PDF de B responde 404 sin
    /// nada de B.
    /// </summary>
    public const string RutaPdfVehiculo = Program.Prefijo + global::Flit.Diagnostico.Api.Modulos.Reportes.ReportesEndpoints.RutaPdfVehiculo;

    /// <summary>
    /// Excel del lote (HU #13849): cada empresa siembra un lote con un vehículo terminado; las celdas del .xlsx
    /// (<see cref="Reportes.ExcelDePrueba.TextoAsync"/>, lector OpenXml) traen el ID del diagnóstico en la hoja «Diagnóstico», y con
    /// el token de A el Excel de B responde 404 sin nada de B.
    /// </summary>
    public const string RutaExcelLote = Program.Prefijo + global::Flit.Diagnostico.Api.Modulos.Reportes.ReportesEndpoints.RutaExcelLote;

    public static IReadOnlyList<EscenarioDeFuga> Todos { get; } =
    [
        EscenarioDeFuga.Con(RutaSonda, _ => new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/sonda" }, l => l.Id),
        new EscenarioDeFuga(RutaSesion, (empresa, _) =>
            Task.FromResult(new Sembrado($"{RutaSesion}?empresa={empresa.Id}", [empresa.Id.ToString()]))),
        EscenarioDeFuga.Con(RutaConfiguracion, _ => new ConfiguracionEmpresa { OperativaActualizadaPor = Guid.NewGuid() }, c => c.OperativaActualizadaPor!.Value),
        new EscenarioDeFuga(RutaTiposTramite, (empresa, _) =>
            Task.FromResult(new Sembrado(RutaTiposTramite, ["TRASPASO_STANDARD"], Exclusivos: [empresa.Id.ToString()]))),
        new EscenarioDeFuga(RutaPlantilla, (empresa, _) =>
            Task.FromResult(new Sembrado(RutaPlantilla, ["[Content_Types].xml", "xl/worksheets/sheet1.xml"], Exclusivos: [empresa.Id.ToString()]))),
        new EscenarioDeFuga(RutaGrilla, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST901", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
            return new Sembrado(Tablero.Tablero.RutaGrilla(lote.Id), [lote.Id.ToString(), lote.Vehiculos.Single().Id.ToString()]);
        }),
        new EscenarioDeFuga(RutaHistorial, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST902", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
            return new Sembrado(Tablero.Tablero.RutaHistorial(lote.Id), [lote.Id.ToString()]);
        }),
        new EscenarioDeFuga(RutaDetalleCelda, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, vehiculos: [new("TST903", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
            var celda = await Tablero.Tablero.PayloadAsync(empresa, lote.Celdas.Single(),
                """{"placa":"TST903","clase":"AUTOMOVIL","propietarios":[],"locatarios":0}""", ct);
            return new Sembrado(Tablero.Tablero.RutaDetalle(lote.Id, celda.VehiculoId, "RUNT"), [celda.Id.ToString(), celda.VehiculoId.ToString()]);
        }),
        new EscenarioDeFuga(RutaListado, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, nombre: "Lote del listado", vehiculos: [new("TST904", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
            return new Sembrado(RutaListado, [lote.Id.ToString()]);
        }),
        new EscenarioDeFuga(RutaPdfVehiculo, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, nombre: "Lote del PDF", vehiculos: [new("TST905", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
            await Tablero.Tablero.PayloadAsync(empresa, lote.Celdas.Single(),
                """{"placa":"TST905","clase":"AUTOMOVIL","propietarios":[],"locatarios":0}""", ct);
            return new Sembrado(Reportes.PdfDePrueba.Ruta(lote.Id, lote.Vehiculos.Single().Id), [lote.Id.ToString()]);
        }, Reportes.PdfDePrueba.TextoAsync),
        new EscenarioDeFuga(RutaExcelLote, async (empresa, ct) =>
        {
            var lote = await Tablero.Tablero.SembrarAsync(empresa, ct, nombre: "Lote del Excel", vehiculos: [new("TST906", [new(Fuente.Runt, EstadoCelda.Pendiente, 1)])]);
            await Tablero.Tablero.PayloadAsync(empresa, lote.Celdas.Single(),
                """{"placa":"TST906","clase":"AUTOMOVIL","propietarios":[],"locatarios":0}""", ct);
            return new Sembrado(Reportes.ExcelDePrueba.Ruta(lote.Id), [lote.Id.ToString()]);
        }, Reportes.ExcelDePrueba.TextoAsync),
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
