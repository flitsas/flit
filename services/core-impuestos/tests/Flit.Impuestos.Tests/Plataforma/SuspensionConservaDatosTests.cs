using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Configuracion;
using Flit.Impuestos.Tests.Credenciales;
using Flit.Impuestos.Tests.Usuarios;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M13; F1b-CF5, F1b-CF6, F1b-CF8, F3-CF8, F4-CF10) — suspender Impuestos a una compañía niega el acceso de sus
/// usuarios (403 <c>PRODUCTO_NO_HABILITADO</c>) y no borra nada: aprovisionamiento, historial, credenciales y lotes en curso
/// siguen en la base. El Super Admin pasa el guard aunque su propia compañía no tenga Impuestos, ve la compañía suspendida en
/// <c>GET /backoffice/empresas</c> (<c>suspendida=true</c>) y lee su historial y el estado de sus credenciales.
/// </summary>
/// <example>
/// <code>
/// api.Productos.Apagar(empresa); // la compañía queda suspendida para el guard
/// var r = await api.Cliente(tokenSuperAdmin).GetAsync(Program.Prefijo + "/backoffice/empresas?pageSize=200", ct);
/// </code>
/// </example>
public sealed class SuspensionConservaDatosTests(ApiUsuariosFixture fixture) : IClassFixture<ApiUsuariosFixture>
{
    public const string RutaEmpresas = Program.Prefijo + "/backoffice/empresas";

    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task El_Super_Admin_ve_las_suspendidas_en_el_backoffice_con_su_estado_accion_y_usuarios()
    {
        var ct = Preparar();
        var (activa, suspendida) = (Guid.NewGuid(), Guid.NewGuid());
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, activa, ct, EstadoAprovisionamiento.ListoSinWebhook);
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, suspendida, ct, EstadoAprovisionamiento.Suspendido);
        Api.Productos.Apagar(suspendida);
        fixture.Directorio.Sembrar(activa, "Activa S.A.S.", "800111222");
        fixture.Plataforma.Conteos = [new ConteoEmpresa { EmpresaId = activa.ToString(), Usuarios = 7, Administradores = 1 }];

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync(RutaEmpresas + "?pageSize=200", ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var json = JsonDocument.Parse(texto);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
        items.Should().ContainKeys(activa, suspendida);

        var a = items[activa];
        a.GetProperty("nombre").GetString().Should().Be("Activa S.A.S.");
        a.GetProperty("nit").GetString().Should().Be("800111222");
        a.GetProperty("suspendida").GetBoolean().Should().BeFalse();
        a.GetProperty("usuarios").GetInt32().Should().Be(7);
        a.GetProperty("aprovisionamiento").GetProperty("estado").GetString().Should().Be("LISTO_SIN_WEBHOOK");
        a.GetProperty("aprovisionamiento").GetProperty("accion").GetString().Should().Be("CARGAR_SECRETO");
        a.GetProperty("aprovisionamiento").GetProperty("firmaConfirmada").GetBoolean().Should().BeFalse();

        var s = items[suspendida];
        s.GetProperty("suspendida").GetBoolean().Should().BeTrue("F4-CF10: las suspendidas siguen visibles para el Super Admin");
        s.GetProperty("aprovisionamiento").GetProperty("estado").GetString().Should().Be("SUSPENDIDO");
        s.GetProperty("aprovisionamiento").GetProperty("accion").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Theory]
    [InlineData(EstadoAprovisionamiento.Error, "REINTENTAR")]
    [InlineData(EstadoAprovisionamiento.Incierto, "REINTENTAR")]
    [InlineData(EstadoAprovisionamiento.RequiereCredenciales, "CARGAR_CREDENCIALES")]
    [InlineData(EstadoAprovisionamiento.RequiereRevisionNit, "CORREGIR_NIT")]
    [InlineData(EstadoAprovisionamiento.Listo, null)]
    [InlineData(EstadoAprovisionamiento.Pendiente, null)]
    public async Task La_accion_del_Super_Admin_sale_de_la_tabla_del_plan(EstadoAprovisionamiento estado, string? accion)
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, estado);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync(RutaEmpresas + "?pageSize=200", ct);

        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == empresa);
        var valor = item.GetProperty("aprovisionamiento").GetProperty("accion");
        (valor.ValueKind == JsonValueKind.Null ? null : valor.GetString()).Should().Be(accion);
    }

    [Fact]
    public async Task Suspender_niega_a_sus_usuarios_y_conserva_aprovisionamiento_historial_credenciales_y_lotes_en_curso()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(Api, empresa, ct);
        var lote = await SembrarLoteEnCursoAsync(empresa, ct);
        await using (var db = Api.Contexto(empresa))
        {
            var fila = await db.Set<AprovisionamientoEmpresa>().SingleAsync(ct);
            fila.EstadoPrevio = fila.Estado;
            fila.Estado = EstadoAprovisionamiento.Suspendido;
            db.Add(new HistorialAprovisionamiento
            {
                TenantId = empresa, Desde = EstadoAprovisionamiento.Listo, Hacia = EstadoAprovisionamiento.Suspendido,
                MotivoCodigo = "PRODUCTO_DESACTIVADO", ActorTipo = "PLATAFORMA", En = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }

        Api.Productos.Apagar(empresa);

        using var admin = await Api.Cliente(UsuariosEndpointsTests.TokenAdmin(Api, empresa)).GetAsync(Program.Prefijo + "/usuarios", ct);
        var cuerpo = await admin.Content.ReadAsStringAsync(ct);
        admin.StatusCode.Should().Be(HttpStatusCode.Forbidden, cuerpo);
        cuerpo.Should().Contain("PRODUCTO_NO_HABILITADO");
        fixture.Plataforma.Pedidos.Should().BeEmpty("el guard niega antes de delegar");

        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));
        using var detalle = await sa.GetAsync($"{RutaEmpresas}/{empresa}/aprovisionamiento", ct);
        using var credenciales = await sa.GetAsync($"{RutaEmpresas}/{empresa}/credenciales", ct);

        var textoDetalle = await detalle.Content.ReadAsStringAsync(ct);
        detalle.StatusCode.Should().Be(HttpStatusCode.OK, textoDetalle);
        using var json = JsonDocument.Parse(textoDetalle);
        json.RootElement.GetProperty("estado").GetString().Should().Be("SUSPENDIDO");
        var historial = json.RootElement.GetProperty("historial").EnumerateArray().ToList();
        historial.Should().ContainSingle("F1b-CF8: el historial sigue visible para el Super Admin");
        historial[0].GetProperty("desde").GetString().Should().Be("LISTO");
        historial[0].GetProperty("hacia").GetString().Should().Be("SUSPENDIDO");
        historial[0].GetProperty("motivo").GetString().Should().Be("PRODUCTO_DESACTIVADO");
        historial[0].GetProperty("actorTipo").GetString().Should().Be("PLATAFORMA");

        credenciales.StatusCode.Should().Be(HttpStatusCode.OK);
        using var jsonCred = JsonDocument.Parse(await credenciales.Content.ReadAsStringAsync(ct));
        jsonCred.RootElement.GetProperty("tieneApiKey").GetBoolean().Should().BeTrue("F1b-CF6: la credencial se conserva cifrada");

        await using var lectura = Api.Contexto(empresa);
        (await lectura.Set<CredencialEmpresa>().CountAsync(ct)).Should().Be(1);
        (await lectura.Set<AprovisionamientoEmpresa>().SingleAsync(ct)).EstadoPrevio.Should().Be(EstadoAprovisionamiento.Listo);
        (await lectura.Set<Liquidacion>().SingleAsync(l => l.Id == lote, ct)).Estado.Should().Be(EstadoLote.EnProceso, "F3-CF8: el lote en curso no se toca");
    }

    [Fact]
    public async Task El_Super_Admin_pasa_el_guard_aunque_su_compania_no_tenga_Impuestos()
    {
        var ct = Preparar();
        var flit = Guid.NewGuid();
        Api.Productos.Apagar(flit);
        var token = Api.Token(empresa: flit, roles: [], permisos: [], superAdmin: true, tipoEmpresa: "FLIT", usuario: CargaManualCredencialesTests.ActorSuperAdmin);

        using var r = await Api.Cliente(token).GetAsync(RutaEmpresas, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Si_los_conteos_de_la_plataforma_fallan_la_lista_sale_sin_el_campo_usuarios()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Listo);
        fixture.Plataforma.ConteosFallan = true;

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync(RutaEmpresas + "?pageSize=200", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var item = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == empresa);
        item.TryGetProperty("usuarios", out _).Should().BeFalse("sin conteo no se inventa un 0");
    }

    [Fact]
    public async Task La_lista_filtra_por_nombre_o_NIT_y_pagina()
    {
        var ct = Preparar();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var marca = Guid.NewGuid().ToString("N")[..8];
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, a, ct, EstadoAprovisionamiento.Listo);
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, b, ct, EstadoAprovisionamiento.Listo);
        fixture.Directorio.Sembrar(a, $"Álamo {marca}", "901000001");
        fixture.Directorio.Sembrar(b, $"Bosque {marca}", "901000002");
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var porNombre = await sa.GetAsync($"{RutaEmpresas}?q=alamo {marca}", ct);
        using var porMarca = await sa.GetAsync($"{RutaEmpresas}?q={marca}&page=2&pageSize=1", ct);
        using var invalida = await sa.GetAsync($"{RutaEmpresas}?pageSize=0", ct);

        using var j1 = JsonDocument.Parse(await porNombre.Content.ReadAsStringAsync(ct));
        j1.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(a);
        using var j2 = JsonDocument.Parse(await porMarca.Content.ReadAsStringAsync(ct));
        j2.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        j2.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(b);
        await ConfiguracionTests.DebeSerRegla(invalida, HttpStatusCode.BadRequest, "PAGINACION_INVALIDA", "La paginación no es válida.", null, ct);
    }

    [Fact]
    public async Task El_detalle_de_una_compania_sin_Impuestos_es_404_EMPRESA_NO_ENCONTRADA()
    {
        var ct = Preparar();

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync($"{RutaEmpresas}/{Guid.NewGuid()}/aprovisionamiento", ct);

        await ConfiguracionTests.DebeSerRegla(r, HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.", null, ct);
    }

    [Fact]
    public async Task El_detalle_trae_la_empresa_con_su_NIT_y_el_historial_mas_reciente_primero()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Listo);
        fixture.Directorio.Sembrar(empresa, "Detalle S.A.", null);
        var antes = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var db = Api.Contexto(empresa))
        {
            db.Add(new HistorialAprovisionamiento { TenantId = empresa, Desde = null, Hacia = EstadoAprovisionamiento.Pendiente, ActorTipo = "PLATAFORMA", En = antes });
            db.Add(new HistorialAprovisionamiento
            {
                TenantId = empresa, Desde = EstadoAprovisionamiento.Pendiente, Hacia = EstadoAprovisionamiento.Listo, MotivoCodigo = "CARGA_MANUAL",
                ActorTipo = "SUPER_ADMIN", En = antes.AddMinutes(1),
            });
            await db.SaveChangesAsync(ct);
        }

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync($"{RutaEmpresas}/{empresa}/aprovisionamiento", ct);

        var texto = await r.Content.ReadAsStringAsync(ct);
        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        using var json = JsonDocument.Parse(texto);
        var raiz = json.RootElement;
        raiz.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        raiz.GetProperty("empresa").GetProperty("nombre").GetString().Should().Be("Detalle S.A.");
        raiz.GetProperty("empresa").GetProperty("nit").ValueKind.Should().Be(JsonValueKind.Null);
        raiz.GetProperty("modoRespaldoFirma").GetBoolean().Should().BeFalse();
        raiz.TryGetProperty("actualizadoEn", out _).Should().BeTrue();
        var historial = raiz.GetProperty("historial").EnumerateArray().ToList();
        historial.Select(h => h.GetProperty("hacia").GetString()).Should().Equal("LISTO", "PENDIENTE");
        historial[0].GetProperty("motivo").ValueKind.Should().Be(JsonValueKind.Null, "un motivo fuera del enum del contrato no se expone");
        historial[1].GetProperty("desde").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<Guid> SembrarLoteEnCursoAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = Api.Contexto(empresa);
        var lote = new Liquidacion
        {
            TenantId = empresa, Tipo = TipoLiquidacion.Masiva, Estado = EstadoLote.EnProceso, TotalItems = 3, CPendientes = 3,
            CreadaPor = Guid.NewGuid(), CreadaEn = DateTimeOffset.UtcNow,
        };
        db.Add(lote);
        await db.SaveChangesAsync(ct);
        return lote.Id;
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        fixture.Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}
