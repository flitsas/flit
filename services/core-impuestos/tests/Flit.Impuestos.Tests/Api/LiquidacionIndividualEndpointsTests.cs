using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (M09; F2a-CF1, F1b-CF4, deltas 2 y 4 de impuestos.v1.yaml 1.1.0) — <c>POST /liquidaciones/individual</c> con solo la
/// placa: la normaliza, exige aprovisionamiento <c>LISTO</c> o <c>LISTO_SIN_WEBHOOK</c> (409 <c>APROVISIONAMIENTO_NO_LISTO</c>; 409
/// <c>PRODUCTO_SUSPENDIDO</c> si está suspendido), crea un lote <c>INDIVIDUAL</c> de un ítem con la copia de la configuración,
/// libera su crédito y responde 201 con <c>items[0]</c>. Placa inválida: 422 <c>PLACA_INVALIDA</c>. El Super Admin liquida con
/// <c>?empresa</c> (obligatoria para él: 400 <c>EMPRESA_REQUERIDA</c>; 404 <c>EMPRESA_NO_ENCONTRADA</c>); otro usuario que la envía
/// recibe 403 <c>EMPRESA_SOLO_SUPER_ADMIN</c>. El grid y el detalle traen <c>itemId</c> y <c>estadoItem</c> solo en la individual.
/// Todo <c>error</c> es el literal de <c>x-flit-literales</c>.
/// </summary>
/// <example>
/// <code>
/// var r = await cliente.PostAsJsonAsync(Program.Prefijo + "/liquidaciones/individual", new { placa = " abc-123 " });
/// </code>
/// </example>
public sealed class LiquidacionIndividualEndpointsTests
{
    private const string Ruta = Program.Prefijo + "/liquidaciones/individual";
    private const string Lista = Program.Prefijo + "/liquidaciones";
    private static readonly string[] Gestor = [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear];

    [Theory]
    [InlineData(EstadoAprovisionamiento.Listo)]
    [InlineData(EstadoAprovisionamiento.ListoSinWebhook)]
    public async Task Con_aprovisionamiento_listo_crea_el_lote_individual_y_responde_201_con_su_item(EstadoAprovisionamiento estado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, estado);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor, usuario: usuario)).PostAsJsonAsync(Ruta, new { placa = " abc-123 " }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var raiz = cuerpo.RootElement;
        raiz.GetProperty("tipo").GetString().Should().Be("INDIVIDUAL");
        raiz.GetProperty("estado").GetString().Should().Be("EN_PROCESO", "el ítem recibió crédito al crear");
        raiz.GetProperty("totalItems").GetInt32().Should().Be(1);
        raiz.GetProperty("placa").GetString().Should().Be("ABC123");
        raiz.GetProperty("creadaPor").GetProperty("id").GetGuid().Should().Be(usuario);
        raiz.GetProperty("creadaPor").GetProperty("nombre").GetString().Should().Be("operador@empresa.test", "creada_por_nombre (1.2.0)");
        raiz.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        raiz.GetProperty("contadores").GetProperty("pendientes").GetInt32().Should().Be(1);
        raiz.GetProperty("contadores").GetProperty("noAplica").GetInt32().Should().Be(0, "contador de la 1.3.0");
        var copia = raiz.GetProperty("configCopia");
        copia.GetProperty("maxRegistrosLote").GetInt32().Should().Be(500);
        copia.GetProperty("departamentosHabilitados").ValueKind.Should().Be(JsonValueKind.Null);
        copia.GetProperty("concurrencia").GetInt32().Should().Be(2);
        var item = raiz.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("placa").GetString().Should().Be("ABC123");
        item.GetProperty("estado").GetString().Should().Be("CONSULTANDO_RUNT");
        item.GetProperty("liquidacionId").GetGuid().Should().Be(raiz.GetProperty("id").GetGuid());
        raiz.GetProperty("itemId").GetGuid().Should().Be(item.GetProperty("id").GetGuid());
        raiz.GetProperty("estadoItem").GetString().Should().Be("CONSULTANDO_RUNT");
        r.Headers.Location!.ToString().Should().EndWith("/liquidaciones/" + raiz.GetProperty("id").GetGuid());

        var solicitud = (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().ContainSingle().Subject;
        solicitud.Payload.Should().NotContain("ABC123", "el evento lleva solo ids");
    }

    [Fact]
    public async Task Sin_claim_name_creada_por_nombre_sale_de_Identidad_y_no_del_correo()
    {
        var ct = TestContext.Current.CancellationToken;
        var identidad = new Flit.Impuestos.Tests.Plataforma.IdentidadUsuarios { Nombre = "Gestora Identidad" };
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba(),
            configurar: b => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(b.Services, identidad.Cliente()));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor, usuario: usuario)).PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(ct));
        using var cuerpo = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        cuerpo.RootElement.GetProperty("creadaPor").GetProperty("nombre").GetString().Should().Be("Gestora Identidad", "el token no trae name: Identidad");
        identidad.UltimaEmpresa.Should().Be(empresa.ToString());
        identidad.UltimoUsuario.Should().Be(usuario.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EstadoAprovisionamiento.Pendiente)]
    [InlineData(EstadoAprovisionamiento.RequiereCredenciales)]
    [InlineData(EstadoAprovisionamiento.Error)]
    [InlineData(EstadoAprovisionamiento.RequiereRevisionNit)]
    public async Task Sin_aprovisionamiento_listo_responde_409_APROVISIONAMIENTO_NO_LISTO_y_no_crea_nada(EstadoAprovisionamiento? estado)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        if (estado is { } e)
            await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, e);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct);

        await EsperarError(r, HttpStatusCode.Conflict, "APROVISIONAMIENTO_NO_LISTO",
            "La compañía todavía no puede liquidar porque su configuración con el proveedor no está lista.");
        await using var db = api.Contexto(empresa);
        (await db.Set<Liquidacion>().CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task Suspendido_responde_409_PRODUCTO_SUSPENDIDO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, EstadoAprovisionamiento.Suspendido);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct);

        await EsperarError(r, HttpStatusCode.Conflict, "PRODUCTO_SUSPENDIDO", "Impuestos está suspendido para la compañía: solo lectura.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB1")]
    [InlineData("ABC.123")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData(null)]
    public async Task Placa_invalida_responde_422_PLACA_INVALIDA_en_el_campo_placa(string? placa)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta, new { placa }, ct);

        var cuerpo = await EsperarError(r, HttpStatusCode.UnprocessableEntity, "PLACA_INVALIDA", "Escribe una placa válida: de 4 a 10 letras o números.");
        cuerpo.GetProperty("campo").GetString().Should().Be("placa");
    }

    [Fact]
    public async Task Sin_el_permiso_de_crear_responde_403_y_con_el_producto_apagado_403_PRODUCTO_NO_HABILITADO()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        (await api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer])).PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        api.Productos.Apagar(empresa);
        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadAsStringAsync(ct)).Should().Contain("PRODUCTO_NO_HABILITADO");
    }

    [Fact]
    public async Task El_Super_Admin_liquida_para_la_compania_de_empresa_y_sin_ella_recibe_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var superAdmin = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT", usuario: superAdmin));

        await EsperarError(await sa.PostAsJsonAsync(Ruta, new { placa = "ABC123" }, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await EsperarError(await sa.PostAsJsonAsync($"{Ruta}?empresa=no-es-uuid", new { placa = "ABC123" }, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await EsperarError(await sa.PostAsJsonAsync($"{Ruta}?empresa={Guid.NewGuid()}", new { placa = "ABC123" }, ct),
            HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.");

        var r = await sa.PostAsJsonAsync($"{Ruta}?empresa={empresa}", new { placa = "ABC123" }, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = api.Contexto(empresa);
        var lote = await db.Set<Liquidacion>().AsNoTracking().SingleAsync(ct);
        lote.TenantId.Should().Be(empresa);
        lote.CreadaPor.Should().Be(superAdmin);
        (await db.Set<LiquidacionItem>().AsNoTracking().SingleAsync(ct)).TenantId.Should().Be(empresa);
    }

    [Fact]
    public async Task Quien_no_es_Super_Admin_y_manda_empresa_recibe_403_EMPRESA_SOLO_SUPER_ADMIN()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);

        var r = await api.Cliente(api.Token(empresa: empresa, permisos: Gestor)).PostAsJsonAsync($"{Ruta}?empresa={empresa}", new { placa = "ABC123" }, ct);

        await EsperarError(r, HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
    }

    [Fact]
    public async Task El_grid_y_el_detalle_traen_itemId_y_estadoItem_solo_en_la_individual()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (individual, itemsIndividual) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["ABC123"], ct, EstadoItem.Exitoso, DateTimeOffset.UtcNow.AddMinutes(-1));
        var (masiva, _) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA111", "BBB222"], ct, creadaEn: DateTimeOffset.UtcNow);
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer]));

        using var pagina = JsonDocument.Parse(await cliente.GetStringAsync(Lista, ct));

        pagina.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        pagina.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        pagina.RootElement.GetProperty("pageSize").GetInt32().Should().Be(50);
        var filas = pagina.RootElement.GetProperty("items").EnumerateArray().ToList();
        filas.Select(f => f.GetProperty("id").GetGuid()).Should().Equal([masiva, individual], "orden por creadaEn descendente");
        filas[0].GetProperty("tipo").GetString().Should().Be("MASIVA");
        filas[0].GetProperty("itemId").ValueKind.Should().Be(JsonValueKind.Null);
        filas[0].GetProperty("estadoItem").ValueKind.Should().Be(JsonValueKind.Null);
        filas[0].GetProperty("placa").ValueKind.Should().Be(JsonValueKind.Null);
        filas[0].GetProperty("nombreArchivo").GetString().Should().Be("placas.csv");
        filas[1].GetProperty("itemId").GetGuid().Should().Be(itemsIndividual[0]);
        filas[1].GetProperty("estadoItem").GetString().Should().Be("EXITOSO");
        filas[1].GetProperty("placa").GetString().Should().Be("ABC123");

        using var detalle = JsonDocument.Parse(await cliente.GetStringAsync($"{Lista}/{individual}", ct));
        detalle.RootElement.GetProperty("itemId").GetGuid().Should().Be(itemsIndividual[0]);
        detalle.RootElement.GetProperty("estadoItem").GetString().Should().Be("EXITOSO");
        detalle.RootElement.GetProperty("configCopia").GetProperty("maxRegistrosLote").GetInt32().Should().Be(500);

        using var items = JsonDocument.Parse(await cliente.GetStringAsync($"{Lista}/{masiva}/items", ct));
        items.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        items.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("placa").GetString()).Should().Equal("AAA111", "BBB222");

        using var filtrada = JsonDocument.Parse(await cliente.GetStringAsync($"{Lista}?tipo=INDIVIDUAL&placa=abc-1", ct));
        filtrada.RootElement.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).Should().Equal(individual);
    }

    [Fact]
    public async Task El_grid_marca_placaNoInscrita_solo_en_la_individual_con_error_de_placa_no_inscrita()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var ahora = DateTimeOffset.UtcNow;
        var (noInscrita, _) = await Reportes.EntornoReportes.SembrarAsync(api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, ahora.AddMinutes(-3),
            new Reportes.ItemSemilla("NOI123", EstadoItem.Error, ErrorCodigo: ClasificacionErrores.CodigoPortalRechazo,
                ErrorMensaje: "La placa ingresada no se encuentra inscrita en nuestra base de datos."));
        var (otroError, _) = await Reportes.EntornoReportes.SembrarAsync(api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, ahora.AddMinutes(-2),
            new Reportes.ItemSemilla("OTR123", EstadoItem.Error, ErrorCodigo: "portal_unavailable", ErrorMensaje: "Mantenimiento del portal."));
        var (masiva, _) = await Reportes.EntornoReportes.SembrarAsync(api, empresa, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, ahora.AddMinutes(-1),
            new Reportes.ItemSemilla("AAA111", EstadoItem.Error, ErrorCodigo: ClasificacionErrores.CodigoPortalRechazo,
                ErrorMensaje: "La placa ingresada no se encuentra inscrita en nuestra base de datos."),
            new Reportes.ItemSemilla("BBB222"));
        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer]));

        using var pagina = JsonDocument.Parse(await cliente.GetStringAsync(Lista, ct));

        var filas = pagina.RootElement.GetProperty("items").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetGuid());
        filas[noInscrita].GetProperty("placaNoInscrita").GetBoolean().Should().BeTrue("ERROR portal_rejected con «no se encuentra inscrita»");
        filas[otroError].GetProperty("placaNoInscrita").GetBoolean().Should().BeFalse("otro error no es placa no inscrita");
        filas[masiva].GetProperty("placaNoInscrita").ValueKind.Should().Be(JsonValueKind.Null, "en la masiva va null, como estadoItem");

        using var detalle = JsonDocument.Parse(await cliente.GetStringAsync($"{Lista}/{noInscrita}", ct));
        detalle.RootElement.GetProperty("placaNoInscrita").GetBoolean().Should().BeTrue("Liquidacion extiende ResumenLiquidacion");
    }

    [Fact]
    public void El_contrato_declara_placaNoInscrita_en_ResumenLiquidacion_desde_la_1_2_3()
    {
        var lineas = File.ReadAllLines(RutaContrato());
        var inicio = Array.FindIndex(lineas, l => l.TrimEnd() == "    ResumenLiquidacion:");
        var fin = Array.FindIndex(lineas, inicio + 1, l => l.Length > 4 && l.StartsWith("    ", StringComparison.Ordinal) && !l.StartsWith("     ", StringComparison.Ordinal));

        inicio.Should().BeGreaterThan(0);
        lineas[inicio..fin].Should().Contain(l => l.TrimStart().StartsWith("placaNoInscrita: { type: boolean, nullable: true", StringComparison.Ordinal));
        var version = lineas.Single(l => l.StartsWith("  version: ", StringComparison.Ordinal))["  version: ".Length..].Trim('"');
        Version.Parse(version).Should().BeGreaterThanOrEqualTo(new Version(1, 2, 3), "placaNoInscrita llegó en la 1.2.3 (la 1.3.0 la conserva)");
    }

    private static string RutaContrato()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi", "impuestos.v1.yaml")))
            dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repo");
        return Path.Combine(dir!.FullName, "contracts", "openapi", "impuestos.v1.yaml");
    }

    [Fact]
    public async Task Filtros_y_paginacion_invalidos_responden_400_con_su_literal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var cliente = api.Cliente(api.Token(permisos: [Permisos.LiquidacionesVer]));

        await EsperarError(await cliente.GetAsync($"{Lista}?tipo=OTRO", ct), HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "Un filtro no es válido.");
        await EsperarError(await cliente.GetAsync($"{Lista}?pageSize=201", ct), HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "La paginación no es válida.");
        await EsperarError(await cliente.GetAsync($"{Lista}?desde=2026-02-01&hasta=2026-01-01", ct), HttpStatusCode.BadRequest, "RANGO_INVALIDO", "El rango de fechas no es válido.");
    }

    internal static async Task<JsonElement> EsperarError(HttpResponseMessage r, HttpStatusCode status, string codigo, string literal)
    {
        var texto = await r.Content.ReadAsStringAsync();
        r.StatusCode.Should().Be(status, texto);
        using var cuerpo = JsonDocument.Parse(texto);
        cuerpo.RootElement.GetProperty("code").GetString().Should().Be(codigo);
        cuerpo.RootElement.GetProperty("error").GetString().Should().Be(literal, "el error es el literal de x-flit-literales");
        return cuerpo.RootElement.Clone();
    }
}
