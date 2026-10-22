using System.Globalization;
using System.Net;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Plataforma;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;

namespace Flit.Diagnostico.Tests.Diagnosticos;

/// <summary>
/// HU #13845 (F08-H1) — <c>GET /diagnosticos</c>: lotes de la compañía del token, del más reciente al más antiguo, con
/// filtros de fecha (días de Colombia, inclusivos), modo, estado y nombre, la visibilidad de <c>AccesoLote</c> (EMPRESA por
/// defecto; PROPIOS, solo los suyos; el admin de la compañía, todos), el tipo de trámite con su nombre y el nombre del
/// creador por Identidad. Placas, nombres y usuarios ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ListadoDiagnosticosTests
/// </code>
/// </example>
public sealed class ListadoDiagnosticosTests
{
    // AC1 (F08-C1, F08-C3) — A con tres lotes y B con dos, sin visibilidad configurada: el usuario de A ve solo los tres de A,
    // del más reciente al más antiguo, con modo, tipo de trámite si aplica, estado y nombre del creador.
    [Fact]
    public async Task Lista_solo_la_compania_del_token_ordenada_por_creacion()
    {
        var identidad = new UsuariosIdentidadDePrueba();
        await using var api = await ListadoDePrueba.ArrancarAsync(identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var creador = Guid.NewGuid();
        identidad.Nombres[creador] = "Ana Prueba";
        var antiguo = await ListadoDePrueba.LoteAsync(empresas.A, "Lote antiguo", ListadoDePrueba.Instante("2026-10-19T08:00:00-05:00"), ct, creador);
        var reciente = await ListadoDePrueba.LoteAsync(empresas.A, "Lote reciente", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct, creador,
            ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD", EstadoDiagnostico.Completado);
        var medio = await ListadoDePrueba.LoteAsync(empresas.A, "Lote medio", ListadoDePrueba.Instante("2026-10-20T08:00:00-05:00"), ct, creador,
            estado: EstadoDiagnostico.Incompleto);
        var deB1 = await ListadoDePrueba.LoteAsync(empresas.B, "Lote de B uno", ListadoDePrueba.Instante("2026-10-21T09:00:00-05:00"), ct);
        var deB2 = await ListadoDePrueba.LoteAsync(empresas.B, "Lote de B dos", ListadoDePrueba.Instante("2026-10-18T09:00:00-05:00"), ct);

        var respuesta = await empresas.A.Cliente().GetAsync(ListadoDePrueba.Ruta, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        var json = JsonDocument.Parse(cuerpo).RootElement;
        json.GetProperty("pagina").GetInt32().Should().Be(1);
        json.GetProperty("tamano").GetInt32().Should().Be(25);
        json.GetProperty("total").GetInt32().Should().Be(3);
        var items = json.GetProperty("diagnosticos").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(reciente.Id, medio.Id, antiguo.Id);
        cuerpo.Should().NotContainAny([deB1.Id.ToString(), deB2.Id.ToString(), "Lote de B", empresas.B.Id.ToString()]);
        cuerpo.Should().NotContainAny(["configuracion", "alcance", "documento"], "el listado no trae configuración ni documentos");
        var primero = items[0];
        primero.GetProperty("nombre").GetString().Should().Be("Lote reciente");
        primero.GetProperty("modo").GetString().Should().Be("CON_ANALISIS");
        primero.GetProperty("estado").GetString().Should().Be("COMPLETADO");
        primero.GetProperty("tipo_tramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
        primero.GetProperty("creado_en").GetDateTimeOffset().Should().Be(reciente.CreadoEn);
        primero.GetProperty("usuario_creador").GetProperty("id").GetGuid().Should().Be(creador);
        primero.GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("Ana Prueba");
        items[1].GetProperty("modo").GetString().Should().Be("SIMPLE");
        items[1].GetProperty("estado").GetString().Should().Be("INCOMPLETO");
        items[1].GetProperty("tipo_tramite").ValueKind.Should().Be(JsonValueKind.Null);

        var pagina2 = await ListadoDePrueba.JsonAsync(await empresas.A.Cliente().GetAsync(ListadoDePrueba.Ruta + "?pagina=2&tamano=2", ct), ct);
        pagina2.GetProperty("total").GetInt32().Should().Be(3);
        pagina2.GetProperty("diagnosticos").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(antiguo.Id);
    }

    // AC2 (F08-C2) — 2026-10-20 23:30 y 2026-10-21 00:10 hora Colombia; desde=hasta=2026-10-21: solo el segundo (inclusivo
    // hasta las 23:59:59 de ese día, y el 2026-10-22 00:00 ya no).
    [Fact]
    public async Task Filtra_por_rango_de_fechas_en_dias_de_Colombia()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ListadoDePrueba.LoteAsync(empresa, "Antes de medianoche", ListadoDePrueba.Instante("2026-10-20T23:30:00-05:00"), ct);
        var segundo = await ListadoDePrueba.LoteAsync(empresa, "Después de medianoche", ListadoDePrueba.Instante("2026-10-21T00:10:00-05:00"), ct);
        var ultimo = await ListadoDePrueba.LoteAsync(empresa, "Último minuto", ListadoDePrueba.Instante("2026-10-21T23:59:30-05:00"), ct);
        await ListadoDePrueba.LoteAsync(empresa, "Día siguiente", ListadoDePrueba.Instante("2026-10-22T00:00:00-05:00"), ct);

        var json = await ListadoDePrueba.JsonAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + "?desde=2026-10-21&hasta=2026-10-21", ct), ct);

        json.GetProperty("total").GetInt32().Should().Be(2);
        ListadoDePrueba.Ids(json).Should().Equal(ultimo.Id, segundo.Id);

        var soloDesde = await ListadoDePrueba.JsonAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + "?desde=2026-10-22", ct), ct);
        ListadoDePrueba.Nombres(soloDesde).Should().Equal("Día siguiente");
        var soloHasta = await ListadoDePrueba.JsonAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + "?hasta=2026-10-20", ct), ct);
        ListadoDePrueba.Nombres(soloHasta).Should().Equal("Antes de medianoche");
    }

    // AC1 (F08-C2) — modo, estado y nombre se combinan; el nombre es «contiene» sin distinguir mayúsculas y con % y _ literales.
    [Fact]
    public async Task Combina_modo_estado_y_nombre()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var t = ListadoDePrueba.Instante("2026-10-21T10:00:00-05:00");
        await ListadoDePrueba.LoteAsync(empresa, "Flota Norte", t, ct, estado: EstadoDiagnostico.EnProceso);
        await ListadoDePrueba.LoteAsync(empresa, "flota norte analizada", t.AddMinutes(1), ct, modo: ModoDiagnostico.ConAnalisis, tipo: "TRASPASO_STANDARD", estado: EstadoDiagnostico.Completado);
        await ListadoDePrueba.LoteAsync(empresa, "FLOTA NORTE cerrada", t.AddMinutes(2), ct, estado: EstadoDiagnostico.Completado);
        await ListadoDePrueba.LoteAsync(empresa, "Flota Sur", t.AddMinutes(3), ct, estado: EstadoDiagnostico.Completado);
        await ListadoDePrueba.LoteAsync(empresa, "Cobertura 100% sur", t.AddMinutes(4), ct, estado: EstadoDiagnostico.Completado);
        await ListadoDePrueba.LoteAsync(empresa, "Cobertura 1000 sur", t.AddMinutes(5), ct, estado: EstadoDiagnostico.Completado);
        await ListadoDePrueba.LoteAsync(empresa, "lote a_b", t.AddMinutes(6), ct);
        await ListadoDePrueba.LoteAsync(empresa, "lote axb", t.AddMinutes(7), ct);

        async Task<IReadOnlyList<string>> Listar(string query) =>
            ListadoDePrueba.Nombres(await ListadoDePrueba.JsonAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + query, ct), ct));

        (await Listar("?modo=SIMPLE&estado=COMPLETADO&nombre=norte")).Should().Equal("FLOTA NORTE cerrada");
        (await Listar("?modo=CON_ANALISIS&nombre=FLOTA")).Should().Equal("flota norte analizada");
        (await Listar("?estado=EN_PROCESO&nombre=fLoTa")).Should().Equal("Flota Norte");
        (await Listar("?nombre=" + Uri.EscapeDataString("100%"))).Should().Equal("Cobertura 100% sur");
        (await Listar("?nombre=a_b")).Should().Equal("lote a_b");
        (await Listar("?modo=SIMPLE&estado=COMPLETADO")).Should().Equal("Cobertura 1000 sur", "Cobertura 100% sur", "Flota Sur", "FLOTA NORTE cerrada");
    }

    // AC1 (F08-C3) — sin visibilidad configurada (EMPRESA) un usuario sin permiso de admin ve los lotes de todos los de su compañía.
    [Fact]
    public async Task Empresa_por_defecto_ve_todos_los_de_la_compania()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var usuario1 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var usuario2 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var suyo = await ListadoDePrueba.LoteAsync(empresa, "Del usuario 1", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct, Tablero.Tablero.Usuario(usuario1));
        var ajeno = await ListadoDePrueba.LoteAsync(empresa, "Del usuario 2", ListadoDePrueba.Instante("2026-10-21T09:00:00-05:00"), ct, Tablero.Tablero.Usuario(usuario2));

        var json = await ListadoDePrueba.JsonAsync(await api.Cliente(usuario1).GetAsync(ListadoDePrueba.Ruta, ct), ct);

        ListadoDePrueba.Ids(json).Should().Equal(ajeno.Id, suyo.Id);
    }

    // AC3 (F08-C3, R-21) — PROPIOS: el usuario 1 ve solo los suyos (el total también) y el admin de la compañía ve todos.
    [Fact]
    public async Task Propios_solo_ve_los_suyos_y_el_admin_ve_todos()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await SembrarAsync(empresa, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        var usuario1 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var usuario2 = ListadoDePrueba.TokenUsuario(api, empresa.Id);
        var admin = ListadoDePrueba.TokenAdmin(api, empresa.Id);
        var suyo1 = await ListadoDePrueba.LoteAsync(empresa, "Del usuario 1 a", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct, Tablero.Tablero.Usuario(usuario1));
        var suyo2 = await ListadoDePrueba.LoteAsync(empresa, "Del usuario 1 b", ListadoDePrueba.Instante("2026-10-21T10:00:00-05:00"), ct, Tablero.Tablero.Usuario(usuario1));
        var ajeno = await ListadoDePrueba.LoteAsync(empresa, "Del usuario 2", ListadoDePrueba.Instante("2026-10-21T09:00:00-05:00"), ct, Tablero.Tablero.Usuario(usuario2));

        var deUsuario1 = await ListadoDePrueba.JsonAsync(await api.Cliente(usuario1).GetAsync(ListadoDePrueba.Ruta, ct), ct);
        var deAdmin = await ListadoDePrueba.JsonAsync(await api.Cliente(admin).GetAsync(ListadoDePrueba.Ruta, ct), ct);

        ListadoDePrueba.Ids(deUsuario1).Should().Equal(suyo2.Id, suyo1.Id);
        deUsuario1.GetProperty("total").GetInt32().Should().Be(2, "el total tampoco cuenta los lotes ajenos");
        deUsuario1.GetRawText().Should().NotContainAny([ajeno.Id.ToString(), "Del usuario 2"]);
        ListadoDePrueba.Ids(deAdmin).Should().Equal(suyo2.Id, ajeno.Id, suyo1.Id);
        deAdmin.GetProperty("total").GetInt32().Should().Be(3);
    }

    // AC1 (F08-C1) — tipo de trámite con su nombre del catálogo (el código si ya no está publicado en la etapa) y el nombre del
    // creador por Identidad, «No disponible» si Identidad no lo conoce; una llamada por creador distinto de la página.
    [Fact]
    public async Task Trae_tramite_analizado_y_nombre_del_creador()
    {
        var identidad = new UsuariosIdentidadDePrueba();
        await using var api = await ListadoDePrueba.ArrancarAsync(identidad);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var (ana, desconocido) = (Guid.NewGuid(), Guid.NewGuid());
        identidad.Nombres[ana] = "Ana Prueba";
        var t = ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00");
        var traspaso = await ListadoDePrueba.LoteAsync(empresa, "Con traspaso", t, ct, ana, ModoDiagnostico.ConAnalisis, "TRASPASO_STANDARD");
        var retirado = await ListadoDePrueba.LoteAsync(empresa, "Con tipo fuera de la etapa", t.AddMinutes(1), ct, ana, ModoDiagnostico.ConAnalisis, "MATRICULA_INICIAL");
        var simple = await ListadoDePrueba.LoteAsync(empresa, "Simple", t.AddMinutes(2), ct, desconocido);

        var items = await ListadoDePrueba.DiagnosticosAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta, ct), ct);

        JsonElement Item(CabeceraDiagnostico c) => items.Single(i => i.GetProperty("id").GetGuid() == c.Id);
        Item(traspaso).GetProperty("tipo_tramite").GetProperty("codigo").GetString().Should().Be("TRASPASO_STANDARD");
        Item(traspaso).GetProperty("tipo_tramite").GetProperty("nombre").GetString().Should().Be("TRASPASO");
        Item(retirado).GetProperty("tipo_tramite").GetProperty("nombre").GetString().Should().Be("MATRICULA_INICIAL", "sin publicar, el nombre es el código");
        Item(simple).GetProperty("tipo_tramite").ValueKind.Should().Be(JsonValueKind.Null);
        Item(traspaso).GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("Ana Prueba");
        Item(retirado).GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("Ana Prueba");
        Item(simple).GetProperty("usuario_creador").GetProperty("id").GetGuid().Should().Be(desconocido);
        Item(simple).GetProperty("usuario_creador").GetProperty("nombre").GetString().Should().Be("No disponible");
        identidad.Pedidos.Select(p => p.Usuario).Should().BeEquivalentTo([ana.ToString(), desconocido.ToString()], "una llamada por creador distinto");
        identidad.Pedidos.Should().OnlyContain(p => p.Empresa == empresa.Id.ToString());
    }

    // AC7 (F08-C5, supuesto «Sin lanzar») — un lote en CREADO aparece con estado CREADO sin filtro de estado y no con EN_PROCESO.
    [Fact]
    public async Task Lote_sin_lanzar_solo_aparece_sin_filtro_de_estado()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var sinLanzar = await ListadoDePrueba.LoteAsync(empresa, "Sin lanzar", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct, estado: EstadoDiagnostico.Creado);
        var enProceso = await ListadoDePrueba.LoteAsync(empresa, "En proceso", ListadoDePrueba.Instante("2026-10-21T07:00:00-05:00"), ct, estado: EstadoDiagnostico.EnProceso);

        var todos = await ListadoDePrueba.DiagnosticosAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta, ct), ct);
        var filtrados = await ListadoDePrueba.DiagnosticosAsync(await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + "?estado=EN_PROCESO", ct), ct);

        todos.Select(i => (i.GetProperty("id").GetGuid(), i.GetProperty("estado").GetString()))
            .Should().Equal((sinLanzar.Id, "CREADO"), (enProceso.Id, "EN_PROCESO"));
        filtrados.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(enProceso.Id);
    }

    // AC5 (F08-C4) — rango invertido: 400 RANGO_INVALIDO con campo=hasta; filtros desconocidos o fuera de rango: 400 con su campo.
    [Fact]
    public async Task Rango_invertido_y_filtro_desconocido_responden_400_con_campo()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        (string Query, string Codigo, string Campo)[] casos =
        [
            ("?desde=2026-10-22&hasta=2026-10-01", "RANGO_INVALIDO", "hasta"),
            ("?modo=OTRO", "FILTRO_INVALIDO", "modo"),
            ("?modo=simple", "FILTRO_INVALIDO", "modo"),
            ("?estado=CERRADO", "FILTRO_INVALIDO", "estado"),
            ("?estado=CREADO", "FILTRO_INVALIDO", "estado"),
            ("?desde=2026-13-01", "FILTRO_INVALIDO", "desde"),
            ("?hasta=21/10/2026", "FILTRO_INVALIDO", "hasta"),
            ("?nombre=" + new string('x', 101), "FILTRO_INVALIDO", "nombre"),
            ("?pagina=0", "PAGINACION_INVALIDA", "pagina"),
            ("?tamano=101", "PAGINACION_INVALIDA", "tamano"),
            ("?tamano=0", "PAGINACION_INVALIDA", "tamano"),
        ];

        foreach (var (query, codigo, campo) in casos)
        {
            var respuesta = await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + query, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{query}: {cuerpo}");
            var json = JsonDocument.Parse(cuerpo).RootElement;
            json.GetProperty("code").GetString().Should().Be(codigo, query);
            json.GetProperty("campo").GetString().Should().Be(campo, query);
        }

        var limite = await empresa.Cliente().GetAsync(ListadoDePrueba.Ruta + "?tamano=100&nombre=" + new string('x', 100), ct);
        limite.StatusCode.Should().Be(HttpStatusCode.OK, "100 por página y 100 caracteres de nombre son válidos");
    }

    // AC6 (F08-C4) — sin diagnostico.diagnosticos.ver: 403 sin cuerpo.
    [Fact]
    public async Task Sin_permiso_responde_403()
    {
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ListadoDePrueba.LoteAsync(empresa, "Lote oculto", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct);
        using var cliente = api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.ConfiguracionVer, Permisos.DiagnosticosCrear], roles: ["usuario_diagnostico"]));

        var respuesta = await cliente.GetAsync(ListadoDePrueba.Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await respuesta.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
    }
}

/// <summary>Ayudas del listado (HU #13845): la API con Identidad de prueba, la siembra de cabeceras y la lectura de la respuesta.</summary>
/// <example>
/// <code>
/// await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
/// var lote = await ListadoDePrueba.LoteAsync(empresa, "Lote", ListadoDePrueba.Instante("2026-10-21T08:00:00-05:00"), ct);
/// </code>
/// </example>
internal static class ListadoDePrueba
{
    public const string Ruta = Program.Prefijo + DiagnosticosEndpoints.Ruta;

    /// <summary>La API con el resolutor de nombres real sobre la Identidad de prueba dada.</summary>
    public static Task<ApiDePrueba> ArrancarAsync(UsuariosIdentidadDePrueba identidad) =>
        ApiDePrueba.ConBaseAsync(configurar: b => b.Services.AddSingleton<IResolutorNombresUsuario>(identidad.Resolutor()));

    public static DateTimeOffset Instante(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).ToUniversalTime();

    public static string TokenUsuario(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.DiagnosticosCrear], roles: ["usuario_diagnostico"]);

    public static string TokenAdmin(ApiDePrueba api, Guid empresa) =>
        api.Token(empresa: empresa, permisos: [Permisos.DiagnosticosVer, Permisos.ConfiguracionEditar, Permisos.ConsultasReprocesar], roles: ["admin_diagnostico"]);

    /// <summary>Siembra una cabecera de la empresa con la fecha de creación dada (sin vehículos).</summary>
    public static async Task<CabeceraDiagnostico> LoteAsync(
        EmpresaDePrueba empresa, string nombre, DateTimeOffset creadoEn, CancellationToken ct, Guid? creador = null,
        ModoDiagnostico modo = ModoDiagnostico.Simple, string? tipo = null, EstadoDiagnostico estado = EstadoDiagnostico.EnProceso)
    {
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = nombre,
            Modo = modo,
            TipoTramiteCodigo = tipo,
            Alcance = [Fuente.Runt],
            Configuracion = Tablero.Tablero.Configuracion(),
            Estado = estado,
            UsuarioCreadorId = creador ?? Guid.NewGuid(),
            CreadoEn = creadoEn.ToUniversalTime(),
            CerradoEn = estado is EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto ? creadoEn.ToUniversalTime() : null,
        };
        await using var db = empresa.Db();
        db.Add(cabecera);
        await db.SaveChangesAsync(ct);
        return cabecera;
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    public static async Task<IReadOnlyList<JsonElement>> DiagnosticosAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        [.. (await JsonAsync(respuesta, ct)).GetProperty("diagnosticos").EnumerateArray()];

    public static IReadOnlyList<Guid> Ids(JsonElement listado) =>
        [.. listado.GetProperty("diagnosticos").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];

    public static IReadOnlyList<string> Nombres(JsonElement listado) =>
        [.. listado.GetProperty("diagnosticos").EnumerateArray().Select(i => i.GetProperty("nombre").GetString()!)];
}
