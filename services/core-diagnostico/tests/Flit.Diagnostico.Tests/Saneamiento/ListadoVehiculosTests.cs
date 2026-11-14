using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;
using VehiculoSembrado = Flit.Diagnostico.Tests.Tablero.VehiculoSembrado;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// HU #14576 AC39/AC34 (plan §6.6, C1, C2, SP-5, SP-10) — grilla de placas <c>GET /vehiculos</c>: filtros combinables de lista
/// cerrada, orden por creación del lote descendente y placa, total, estado EN_PROCESO/TERMINADO calculado sobre las celdas, modo
/// con la serialización del OpenAPI, saneamiento desde la columna del vehículo y visibilidad de <c>AccesoLote</c>. Postgres real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Saneamiento.ListadoVehiculosTests
/// </code>
/// </example>
public sealed class ListadoVehiculosTests
{
    private const string Ruta = Program.Prefijo + "/vehiculos";

    private static readonly DateTimeOffset Antes = new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Despues = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    // AC39 — sin filtros: el lote más nuevo primero y, dentro, por placa; cada fila con estado, veredicto, saneamiento y trámite.
    // Cada filtro y una combinación devuelven solo lo que cumple todos, con su total. El lote sin lanzar no sale.
    [Fact]
    public async Task Filtros_combinados_orden_y_forma_de_la_fila()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var (nuevo, viejo) = await SembrarAsync(empresa, ct);
        await TableroAyudas.SembrarAsync(empresa, ct, estado: EstadoDiagnostico.Creado, vehiculos: [new VehiculoSembrado("TST899", [])]);
        var cliente = api.Cliente(DeudaDePrueba.TokenVer(api, empresa.Id));

        var todo = await JsonAsync(cliente, string.Empty, ct);
        todo.GetProperty("total").GetInt32().Should().Be(4);
        Placas(todo).Should().Equal(["TST811", "TST812", "TST801", "TST802"]);
        var fila = Fila(todo, "TST811");
        fila.GetProperty("diagnostico_id").GetGuid().Should().Be(nuevo.Id);
        fila.GetProperty("diagnostico_nombre").GetString().Should().Be("Lote nuevo");
        fila.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        fila.GetProperty("estado").GetString().Should().Be("TERMINADO");
        fila.GetProperty("veredicto").GetString().Should().Be("BLOQUEADO");
        fila.GetProperty("saneamiento").GetProperty("estado").GetString().Should().Be("EN_GESTION");
        fila.GetProperty("saneamiento").GetProperty("elegible").GetBoolean().Should().BeFalse();
        fila.GetProperty("saneamiento").GetProperty("motivo_no_elegible").GetString().Should().Be("YA_SOLICITADO");
        fila.GetProperty("tramite").GetProperty("numero").GetString().Should().Be("TR-0811");
        var enCurso = Fila(todo, "TST802");
        enCurso.GetProperty("estado").GetString().Should().Be("EN_PROCESO");
        enCurso.GetProperty("modo").GetString().Should().Be("SIMPLE");
        enCurso.GetProperty("veredicto").ValueKind.Should().Be(JsonValueKind.Null);
        enCurso.GetProperty("saneamiento").GetProperty("motivo_no_elegible").GetString().Should().Be("NO_TERMINADO");
        enCurso.GetProperty("tramite").ValueKind.Should().Be(JsonValueKind.Null);
        Fila(todo, "TST812").GetProperty("saneamiento").GetProperty("elegible").GetBoolean().Should().BeTrue();
        Fila(todo, "TST812").GetProperty("saneamiento").GetProperty("estado").ValueKind.Should().Be(JsonValueKind.Null);

        (await PlacasAsync(cliente, "?placa=tst-81", ct)).Should().Equal(["TST811", "TST812"]);
        (await PlacasAsync(cliente, "?estado=EN_PROCESO", ct)).Should().Equal(["TST802"]);
        (await PlacasAsync(cliente, "?estado=EN_PROCESO&estado=TERMINADO", ct)).Should().HaveCount(4);
        (await PlacasAsync(cliente, "?veredicto=SIN_DICTAMEN", ct)).Should().Equal(["TST801", "TST802"]);
        (await PlacasAsync(cliente, "?veredicto=VIABLE&veredicto=BLOQUEADO", ct)).Should().Equal(["TST811", "TST812"]);
        (await PlacasAsync(cliente, "?saneamiento=CON", ct)).Should().Equal(["TST811"]);
        (await PlacasAsync(cliente, "?saneamiento=SIN", ct)).Should().Equal(["TST812", "TST801", "TST802"]);
        (await PlacasAsync(cliente, "?saneamiento_estado=EN_GESTION", ct)).Should().Equal(["TST811"]);
        (await PlacasAsync(cliente, "?saneamiento_estado=SANEADO", ct)).Should().BeEmpty();
        (await PlacasAsync(cliente, "?modo=SIMPLE", ct)).Should().Equal(["TST801", "TST802"]);
        (await PlacasAsync(cliente, "?con_tramite=true", ct)).Should().Equal(["TST811"]);
        (await PlacasAsync(cliente, "?con_tramite=false", ct)).Should().Equal(["TST812", "TST801", "TST802"]);
        (await PlacasAsync(cliente, "?desde=2026-11-05", ct)).Should().Equal(["TST811", "TST812"]);
        (await PlacasAsync(cliente, "?hasta=2026-11-05", ct)).Should().Equal(["TST801", "TST802"]);
        (await PlacasAsync(cliente, "?placa=TST8&estado=TERMINADO&modo=CON_ANALISIS&veredicto=VIABLE&con_tramite=false&saneamiento=SIN", ct))
            .Should().Equal(["TST812"]);

        var pagina = await JsonAsync(cliente, "?tamano=1&pagina=2", ct);
        Placas(pagina).Should().Equal(["TST812"]);
        pagina.GetProperty("total").GetInt32().Should().Be(4);
        viejo.Id.Should().NotBe(nuevo.Id);
    }

    // AC39/SP-10 — valores fuera de su lista o rango: 400 con el formato de error existente y su campo. placa=% no devuelve todo.
    [Theory]
    [InlineData("?tamano=101", "PAGINACION_INVALIDA", "tamano")]
    [InlineData("?tamano=0", "PAGINACION_INVALIDA", "tamano")]
    [InlineData("?pagina=100000", "PAGINACION_INVALIDA", "pagina")]
    [InlineData("?pagina=0", "PAGINACION_INVALIDA", "pagina")]
    [InlineData("?estado=XYZ", "FILTRO_INVALIDO", "estado")]
    [InlineData("?veredicto=OTRO", "FILTRO_INVALIDO", "veredicto")]
    [InlineData("?modo=CON_ANALISIS_IA", "FILTRO_INVALIDO", "modo")]
    [InlineData("?saneamiento=QUIZA", "FILTRO_INVALIDO", "saneamiento")]
    [InlineData("?saneamiento_estado=ANULADO", "FILTRO_INVALIDO", "saneamiento_estado")]
    [InlineData("?saneamiento=SIN&saneamiento_estado=SOLICITADO", "FILTRO_INVALIDO", "saneamiento_estado")]
    [InlineData("?con_tramite=si", "FILTRO_INVALIDO", "con_tramite")]
    [InlineData("?placa=%25", "FILTRO_INVALIDO", "placa")]
    [InlineData("?placa=_", "FILTRO_INVALIDO", "placa")]
    [InlineData("?placa=ABCDEFGHIJKLMNOPQRSTU", "FILTRO_INVALIDO", "placa")]
    [InlineData("?desde=2026-11-10&hasta=2026-11-01", "RANGO_INVALIDO", "hasta")]
    [InlineData("?desde=10-11-2026", "FILTRO_INVALIDO", "desde")]
    public async Task Valor_invalido_responde_400_con_su_campo(string query, string codigo, string campo)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;

        var respuesta = await api.Cliente(DeudaDePrueba.TokenVer(api, empresa.Id)).GetAsync(Ruta + query, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, cuerpo);
        var json = JsonElement.Parse(cuerpo);
        json.GetProperty("code").GetString().Should().Be(codigo);
        json.GetProperty("campo").GetString().Should().Be(campo);
    }

    // AC34/AC33 — otra compañía no aparece; con PROPIOS, los lotes de otro usuario de la compañía no se listan ni cuentan en el total;
    // sin diagnosticos.ver, 403.
    [Fact]
    public async Task Otra_compania_y_propios_no_se_listan_ni_cuentan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var yo = DeudaDePrueba.TokenVer(api, empresas.A.Id);
        var otro = DeudaDePrueba.TokenVer(api, empresas.A.Id);
        await TableroAyudas.SembrarAsync(empresas.A, ct, creador: TableroAyudas.Usuario(yo), vehiculos: [new VehiculoSembrado("TST821", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await TableroAyudas.SembrarAsync(empresas.A, ct, creador: TableroAyudas.Usuario(otro), vehiculos: [new VehiculoSembrado("TST822", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await TableroAyudas.SembrarAsync(empresas.B, ct, vehiculos: [new VehiculoSembrado("TST823", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        var empresa = await JsonAsync(api.Cliente(yo), string.Empty, ct);
        Placas(empresa).Should().BeEquivalentTo(["TST821", "TST822"], "visibilidad EMPRESA; la de B nunca");

        await DeudaDePrueba.PropiosAsync(empresas.A, ct);
        var propios = await JsonAsync(api.Cliente(yo), string.Empty, ct);
        Placas(propios).Should().Equal(["TST821"]);
        propios.GetProperty("total").GetInt32().Should().Be(1, "el lote ajeno no cuenta en el total");
        (await PlacasAsync(api.Cliente(yo), "?placa=TST822", ct)).Should().BeEmpty();

        var sinPermiso = api.Token(empresa: empresas.A.Id, permisos: [Flit.Diagnostico.Api.Plataforma.Permisos.ConfiguracionVer], roles: ["usuario_diagnostico"]);
        (await api.Cliente(sinPermiso).GetAsync(Ruta, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Lote viejo (Simple, 2026-11-01): TST801 terminado y TST802 en proceso. Lote nuevo (Con Análisis, 2026-11-10): TST811 terminado,
    /// BLOQUEADO, saneamiento EN_GESTION y trámite; TST812 terminado y VIABLE.
    /// </summary>
    // UA-5 (auditoría visual, HU #14576) — cada fila trae vin e identificador (el valor cargado), nullables, para nombrar una fila sin
    // placa; UA-2 — los tamaños del selector del kit (10, 20, 30, 50) se aceptan.
    [Fact]
    public async Task UA5_la_fila_trae_vin_e_identificador_y_UA2_acepta_los_tamanos_del_kit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos:
            [new VehiculoSembrado("TST821", [new(Fuente.Runt, EstadoCelda.Completado, 1)]), new VehiculoSembrado("TST822", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var sinPlaca = lote.Vehiculos[1].Id;
        await using (var db = empresa.Db())
            await db.Set<VehiculoDiagnostico>().Where(v => v.Id == sinPlaca)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.Placa, (string?)null).SetProperty(v => v.Vin, "9BWZZZ377VT004251")
                    .SetProperty(v => v.IdentificadorOriginal, "9BWZZZ377VT004251").SetProperty(v => v.IdentificadorNorm, "9BWZZZ377VT004251"), ct);
        var cliente = api.Cliente(DeudaDePrueba.TokenVer(api, empresa.Id));

        var todo = await JsonAsync(cliente, string.Empty, ct);

        var conPlaca = Fila(todo, "TST821");
        conPlaca.GetProperty("identificador").GetString().Should().Be("TST821");
        conPlaca.GetProperty("vin").ValueKind.Should().Be(JsonValueKind.Null);
        var porVin = todo.GetProperty("vehiculos").EnumerateArray().Single(f => f.GetProperty("vehiculo_id").GetGuid() == sinPlaca);
        porVin.GetProperty("vin").GetString().Should().Be("9BWZZZ377VT004251");
        porVin.GetProperty("identificador").GetString().Should().Be("9BWZZZ377VT004251");
        foreach (var tamano in new[] { 10, 20, 30, 50 })
            (await cliente.GetAsync($"{Ruta}?tamano={tamano}", ct)).StatusCode.Should().Be(HttpStatusCode.OK, $"tamano={tamano}");
    }

    private static async Task<(CabeceraDiagnostico Nuevo, CabeceraDiagnostico Viejo)> SembrarAsync(EmpresaDePrueba empresa, CancellationToken ct)
    {
        var viejo = await TableroAyudas.SembrarAsync(empresa, ct, nombre: "Lote viejo", vehiculos:
        [
            new VehiculoSembrado("TST801", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
            new VehiculoSembrado("TST802", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.EnCurso, 1)]),
        ]);
        var nuevo = await TableroAyudas.SembrarAsync(empresa, ct, nombre: "Lote nuevo", modo: ModoDiagnostico.ConAnalisis, tipoTramiteCodigo: "TRASPASO", vehiculos:
        [
            new VehiculoSembrado("TST811", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Na, 1)]),
            new VehiculoSembrado("TST812", [new(Fuente.Runt, EstadoCelda.Completado, 1)]),
        ]);
        var bloqueado = nuevo.Vehiculos.Single(v => v.Placa == "TST811").Id;
        var viable = nuevo.Vehiculos.Single(v => v.Placa == "TST812").Id;
        await using var db = empresa.Db();
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == viejo.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreadoEn, Antes), ct);
        await db.Set<CabeceraDiagnostico>().Where(c => c.Id == nuevo.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreadoEn, Despues), ct);
        await db.Set<VehiculoDiagnostico>().Where(v => v.Id == bloqueado).ExecuteUpdateAsync(s => s
            .SetProperty(v => v.SaneamientoEstado, EstadoSaneamiento.EnGestion)
            .SetProperty(v => v.TramiteId, Guid.NewGuid())
            .SetProperty(v => v.TramiteNumero, "TR-0811"), ct);
        db.Add(Dictamen(bloqueado, "BLOQUEADO"));
        db.Add(Dictamen(viable, "VIABLE"));
        await db.SaveChangesAsync(ct);
        return (nuevo.Cabecera, viejo.Cabecera);
    }

    private static DictamenVehiculo Dictamen(Guid vehiculo, string veredicto) => new()
    {
        VehiculoId = vehiculo,
        Version = 1,
        Vigente = true,
        Veredicto = veredicto,
        Alertamientos = "[]",
        CatalogoVersion = 2,
        RedactadoPor = "PLANTILLA",
        EstadoRedaccion = "PENDIENTE",
        CreadoEn = DateTimeOffset.UtcNow,
    };

    private static async Task<JsonElement> JsonAsync(HttpClient cliente, string query, CancellationToken ct)
    {
        var respuesta = await cliente.GetAsync(Ruta + query, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        return JsonElement.Parse(cuerpo);
    }

    private static async Task<IReadOnlyList<string>> PlacasAsync(HttpClient cliente, string query, CancellationToken ct) =>
        Placas(await JsonAsync(cliente, query, ct));

    private static List<string> Placas(JsonElement listado) =>
        [.. listado.GetProperty("vehiculos").EnumerateArray().Select(v => v.GetProperty("placa").GetString()!)];

    private static JsonElement Fila(JsonElement listado, string placa) =>
        listado.GetProperty("vehiculos").EnumerateArray().Single(v => v.GetProperty("placa").GetString() == placa);
}
