using System.Net;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #14563 AC44 (CF-29) — el rediseño no cambia permisos ni visibilidad. Complementa a <see cref="PermisosTests"/> (manifiesto y
/// roles) y a <see cref="PoliticasBackofficeMatrizTests"/> (actor por sección del backoffice):
/// <list type="bullet">
/// <item>Cada ruta de compañía exige exactamente el permiso de siempre; las rutas nuevas de compañía (notificaciones, B8) exigen
/// <c>diagnostico.diagnosticos.ver</c>. La tabla se copia aquí literal: un cambio de permiso hace fallar la prueba.</item>
/// <item>La escritura del ajuste del dictamen sigue solo en el backoffice, con la política de operación FLIT: ninguna ruta de
/// compañía escribe el dictamen.</item>
/// <item>Con visibilidad PROPIOS, la grilla, el detalle y el dictamen de un lote ajeno responden 404 como hoy.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PermisosYVisibilidadSinCambiosTests
/// </code>
/// </example>
public sealed class PermisosYVisibilidadSinCambiosTests
{
    private const string P = Program.Prefijo;

    /// <summary>Método y patrón de cada ruta de compañía → permiso que exige (sin permiso propio: <c>-</c>).</summary>
    private static readonly string[] PermisosDeCompania =
    [
        "* /api/v1/diagnostico/{*path:nonfile} => -",
        "GET /api/v1/diagnostico/configuracion => diagnostico.configuracion.ver",
        "GET /api/v1/diagnostico/diagnosticos => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/evidencias/{eid:guid} => diagnostico.reportes.descargar",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/evidencias/{eid:guid}/archivo => diagnostico.reportes.descargar",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/excel => diagnostico.reportes.descargar",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/grilla => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/historial => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/tramites/operaciones => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/tramites/operaciones/{opId:guid} => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos/{vid:guid}/consultas/{fuente} => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos/{vid:guid}/dictamen => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos/{vid:guid}/pdf => diagnostico.reportes.descargar",
        "GET /api/v1/diagnostico/notificaciones => diagnostico.diagnosticos.ver",
        "GET /api/v1/diagnostico/plantilla => diagnostico.diagnosticos.crear",
        "GET /api/v1/diagnostico/sesion => -",
        "GET /api/v1/diagnostico/tipos-tramite => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/consultas/{cid:guid}/reprocesar => diagnostico.consultas.reprocesar",
        "POST /api/v1/diagnostico/diagnosticos => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/carga => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/carga/validar-filas => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/cerrar-panel => diagnostico.diagnosticos.ver",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/evidencias => diagnostico.reportes.descargar",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/reintentar-pegadas => diagnostico.consultas.reprocesar",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/tramites => diagnostico.derivacion.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/tramites/operaciones/{opId:guid}/reintentar-fallidas => diagnostico.derivacion.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos/{vid:guid}/reintentar => diagnostico.consultas.reprocesar",
        "POST /api/v1/diagnostico/diagnosticos/{id:guid}/vehiculos/{vid:guid}/tramite => diagnostico.derivacion.crear",
        "POST /api/v1/diagnostico/express => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/express/estimado => diagnostico.diagnosticos.crear",
        "POST /api/v1/diagnostico/notificaciones/leer-todas => diagnostico.diagnosticos.ver",
        "POST /api/v1/diagnostico/notificaciones/{nid:guid}/leida => diagnostico.diagnosticos.ver",
        "PUT /api/v1/diagnostico/configuracion => diagnostico.configuracion.editar",
        "PUT /api/v1/diagnostico/diagnosticos/{id:guid} => diagnostico.diagnosticos.crear",
    ];

    /// <summary>Rutas del backoffice que exigen la política de operación FLIT (escritura sobre datos de una compañía).</summary>
    private static readonly string[] OperacionFlit =
    [
        "GET /api/v1/diagnostico/backoffice/consultas/{consulta:guid}/manual",
        "PUT /api/v1/diagnostico/backoffice/consultas/{consulta:guid}/manual",
        "PUT /api/v1/diagnostico/backoffice/diagnosticos/{id:guid}/vehiculos/{vid:guid}/dictamen",
    ];

    [Fact]
    public async Task HU14563_AC44_cada_ruta_de_compania_exige_el_permiso_de_siempre()
    {
        await using var api = await ApiDePrueba.SinBaseAsync(configurar: ConDerivacion);

        var reales = Rutas(api).Where(r => !r.Patron.Contains("/backoffice", StringComparison.Ordinal) && r.Patron.StartsWith(P, StringComparison.Ordinal))
            .Select(r => $"{r.Clave} => {(r.Permisos.Count == 0 ? "-" : string.Join(",", r.Permisos.Order(StringComparer.Ordinal)))}")
            .Order(StringComparer.Ordinal).ToList();

        string.Join('\n', reales).Should().Be(string.Join('\n', PermisosDeCompania), "ninguna ruta de compañía cambió de permiso");
        reales.Where(r => r.Contains("/notificaciones", StringComparison.Ordinal)).Should()
            .HaveCount(3).And.OnlyContain(r => r.EndsWith(" => " + Permisos.DiagnosticosVer, StringComparison.Ordinal),
                "las rutas nuevas de compañía (B8) exigen diagnostico.diagnosticos.ver");
    }

    [Fact]
    public async Task HU14563_AC44_la_escritura_del_ajuste_sigue_solo_para_FLIT()
    {
        await using var api = await ApiDePrueba.SinBaseAsync();
        var rutas = Rutas(api);

        rutas.Where(r => r.Operacion).Select(r => r.Clave).Order(StringComparer.Ordinal).Should().Equal(OperacionFlit);
        rutas.Should().ContainSingle(r => r.Clave == $"PUT {P}/backoffice{Flit.Diagnostico.Api.Modulos.Backoffice.OverrideDictamenEndpoints.Ruta}")
            .Which.Permisos.Should().Equal(Permisos.DictamenOverride);
        rutas.Where(r => !r.Patron.Contains("/backoffice", StringComparison.Ordinal) && r.Patron.Contains("/dictamen", StringComparison.Ordinal))
            .Should().OnlyContain(r => r.Metodo == "GET", "la compañía solo lee el dictamen");
    }

    [Fact]
    public async Task HU14563_AC44_con_PROPIOS_grilla_detalle_y_dictamen_de_un_lote_ajeno_responden_404()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        var creador = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var otro = api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]);
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: TableroAyudas.Usuario(creador), modo: ModoDiagnostico.ConAnalisis,
            tipoTramiteCodigo: "TRASPASO_STANDARD", vehiculos: [new("TST944", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var vehiculo = lote.Vehiculos.Single().Id;
        await using (var db = empresa.Db())
        {
            db.Add(new DictamenVehiculo
            {
                VehiculoId = vehiculo,
                Version = 1,
                Vigente = true,
                Veredicto = "VIABLE",
                Alertamientos = "[]",
                CatalogoVersion = 1,
                RedactadoPor = "PLANTILLA",
                EstadoRedaccion = "PENDIENTE",
                CreadoEn = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        string[] rutas =
        [
            TableroAyudas.RutaGrilla(lote.Id),
            TableroAyudas.RutaDetalle(lote.Id, vehiculo, "RUNT"),
            $"{P}/diagnosticos/{lote.Id}/vehiculos/{vehiculo}/dictamen",
        ];
        foreach (var ruta in rutas)
        {
            var ajena = await api.Cliente(otro).GetAsync(ruta, ct);
            var cuerpo = await ajena.Content.ReadAsStringAsync(ct);
            ajena.StatusCode.Should().Be(HttpStatusCode.NotFound, ruta);
            cuerpo.Should().NotContain("TST944", ruta);
            (await api.Cliente(creador).GetAsync(ruta, ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"el creador ve {ruta}");
        }
    }

    /// <summary>Con la derivación encendida (sin consumidor) para que sus rutas entren en la tabla.</summary>
    private static void ConDerivacion(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder) =>
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{Flit.Diagnostico.Api.Modulos.Derivacion.OpcionesDerivacion.Seccion}:Habilitada"] = "true",
            [$"{Flit.Diagnostico.Api.Modulos.Derivacion.OpcionesDerivacion.Seccion}:ConsumidorHabilitado"] = "false",
            [$"{Flit.Diagnostico.Api.Modulos.Derivacion.OpcionesDerivacion.Seccion}:UrlTramites"] = "https://tramites.prueba",
        });

    private sealed record Ruta(string Metodo, string Patron, IReadOnlyList<string> Permisos, bool Operacion)
    {
        public string Clave => $"{Metodo} {Patron}";
    }

    private static List<Ruta> Rutas(ApiDePrueba api) =>
        [.. api.App.Services.GetServices<EndpointDataSource>().SelectMany(f => f.Endpoints).OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"]).Select(m => new Ruta(
                m,
                "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                [.. e.Metadata.OfType<AuthorizationPolicy>().SelectMany(p => p.Requirements).OfType<PermisoRequerido>().Select(r => r.Slug).Distinct()],
                e.Metadata.OfType<IAuthorizeData>().Any(a => a.Policy == PoliticaBackoffice.NombreOperacion))))];
}
