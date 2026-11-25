using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Reportes;

/// <summary>
/// HU #14694 (M14; delta 12 de impuestos.v1.yaml 1.1.0) — <c>GET /catalogos/reportes</c> con <c>impuestos.reportes.ver</c> (también el
/// Gestor): <c>departamentos</c> de la caché de <c>/fuentes</c> (lista vacía, nunca 409 ni 503, si la compañía no está lista) y
/// <c>usuarios</c> = quienes crearon liquidaciones en la compañía, solo id y nombre (el id si la plataforma no da el nombre), sin
/// correo. El Super Admin sin <c>empresa</c> recibe las opciones de todas las compañías.
/// </summary>
/// <example>
/// <code>
/// var catalogos = await EntornoReportes.GetJsonAsync(cliente, "/api/v1/impuestos/catalogos/reportes", ct);
/// </code>
/// </example>
public sealed class CatalogoReportesTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task El_Gestor_recibe_departamentos_de_la_cache_y_los_creadores_con_id_y_nombre_sin_correo()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var ana = Guid.NewGuid();
        var sinNombre = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        fixture.Plataforma.Filas.Add(UsuariosPlataformaDePrueba.Fila(ana, "Ana Gestora", "ana@empresa.test", "gestor_impuestos", Flit.Platform.Grpc.V1.EstadoUsuarioProducto.Suspendido));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Masiva, ana, ct, null, new ItemSemilla("CAT001"));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, ana, ct, null, new ItemSemilla("CAT002"));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, sinNombre, ct, DateTimeOffset.UtcNow.AddDays(-200), new ItemSemilla("CAT003"));

        using var r = await Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.ReportesVer])).GetAsync(E.Catalogos, ct);
        var texto = await r.Content.ReadAsStringAsync(ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, texto);
        texto.Should().NotContain("ana@empresa.test");
        using var doc = JsonDocument.Parse(texto);
        doc.RootElement.GetProperty("departamentos").EnumerateArray().Select(d => d.GetProperty("codigo").GetString()).Should().Equal("05", "25", "41");
        doc.RootElement.GetProperty("departamentos")[0].GetProperty("nombre").GetString().Should().Be("Antioquia");
        var usuarios = doc.RootElement.GetProperty("usuarios").EnumerateArray()
            .Select(u => (u.GetProperty("id").GetGuid(), u.GetProperty("nombre").GetString())).ToList();
        usuarios.Should().BeEquivalentTo([(ana, "Ana Gestora"), (sinNombre, sinNombre.ToString())], "una vez cada creador, incluidos los suspendidos");
        doc.RootElement.GetProperty("usuarios")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["id", "nombre"]);
    }

    [Fact]
    public async Task El_nombre_del_creador_sale_de_creada_por_nombre_antes_que_de_la_plataforma()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        var luis = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct);
        fixture.Plataforma.Filas.Add(UsuariosPlataformaDePrueba.Fila(luis, "Luis Plataforma", "luis@empresa.test", "gestor_impuestos"));
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, luis, "Luis Token", ct, null, new ItemSemilla("CAT030"));

        var c = await E.GetJsonAsync(Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.ReportesVer])), E.Catalogos, ct);

        var u = c.GetProperty("usuarios").EnumerateArray().Should().ContainSingle().Subject;
        u.GetProperty("id").GetGuid().Should().Be(luis);
        u.GetProperty("nombre").GetString().Should().Be("Luis Token");
    }

    [Fact]
    public async Task Una_compania_suspendida_recibe_departamentos_vacios_y_nunca_409()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, empresa, ct, EstadoAprovisionamiento.Suspendido);
        await E.SembrarAsync(Api, empresa, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null, new ItemSemilla("CAT010"));

        var c = await E.GetJsonAsync(Api.Cliente(Api.Token(empresa: empresa, permisos: [Permisos.ReportesVer])), E.Catalogos, ct);

        c.GetProperty("departamentos").GetArrayLength().Should().Be(0);
        c.GetProperty("usuarios").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task El_Super_Admin_sin_empresa_recibe_las_opciones_de_todas_las_companias()
    {
        var ct = Preparar();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var creadorA = Guid.NewGuid();
        var creadorB = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, a, ct);
        await E.SembrarEmpresaAsync(Api, b, ct);
        await E.SembrarAsync(Api, a, TipoLiquidacion.Individual, creadorA, ct, null, new ItemSemilla("CAT020"));
        await E.SembrarAsync(Api, b, TipoLiquidacion.Individual, creadorB, ct, null, new ItemSemilla("CAT021"));
        var sa = Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT"));

        var global = await E.GetJsonAsync(sa, E.Catalogos, ct);
        var deA = await E.GetJsonAsync(sa, $"{E.Catalogos}?empresa={a}", ct);

        global.GetProperty("usuarios").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).Should().Contain([creadorA, creadorB]);
        global.GetProperty("departamentos").GetArrayLength().Should().BeGreaterThan(0);
        deA.GetProperty("usuarios").EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).Should().Equal(creadorA);
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync($"{E.Catalogos}?empresa=x", ct),
            HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await Api.Cliente(Api.Token(empresa: a, permisos: [Permisos.ReportesVer])).GetAsync($"{E.Catalogos}?empresa={b}", ct),
            HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
    }

    [Fact]
    public async Task Sin_reportes_ver_responde_403()
    {
        var ct = Preparar();

        using var r = await Api.Cliente(Api.Token(permisos: [Permisos.ConfiguracionVer, Permisos.DashboardVer])).GetAsync(E.Catalogos, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        fixture.Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}
