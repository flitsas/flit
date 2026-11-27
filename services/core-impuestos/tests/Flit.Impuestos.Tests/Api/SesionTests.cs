using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Api;

/// <summary>
/// HU #14694 (M05, plan §5.1) — <c>GET /api/v1/impuestos/sesion</c>: usuario, compañía, roles, permisos del producto,
/// <c>esSuperAdmin</c> y el estado de aprovisionamiento de la compañía (<c>aprovisionamiento{estado, firmaConfirmada}</c>, que
/// sale de <c>impuestos.aprovisionamientos</c>). Una sesión = una compañía: ningún parámetro ni cabecera la cambia.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~SesionTests
/// </code>
/// </example>
public sealed class SesionTests
{
    private const string Ruta = Program.Prefijo + "/sesion";

    private static readonly string[] PermisosGestor =
    [
        "impuestos.dashboard.ver", "impuestos.liquidaciones.ver", "impuestos.liquidaciones.crear", "impuestos.recibos.descargar",
        "impuestos.reportes.ver", "impuestos.reportes.exportar",
    ];

    [Fact]
    public async Task Devuelve_usuario_compania_roles_permisos_y_aprovisionamiento()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await using (var db = api.Contexto(empresa))
        {
            db.Add(new AprovisionamientoEmpresa { TenantId = empresa, Estado = EstadoAprovisionamiento.ListoSinWebhook, FirmaConfirmada = false });
            await db.SaveChangesAsync(ct);
        }

        var token = api.Token(empresa: empresa, usuario: usuario, roles: ["gestor_impuestos", "AdminCompany"], permisos: [.. PermisosGestor, "users.read"]);
        var respuesta = await api.Cliente(token).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesion = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        sesion.GetProperty("usuario").GetProperty("id").GetGuid().Should().Be(usuario);
        sesion.GetProperty("usuario").GetProperty("email").GetString().Should().Be("operador@empresa.test");
        sesion.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresa);
        sesion.GetProperty("empresa").GetProperty("nombre").GetString().Should().Be("Empresa de prueba");
        Cadenas(sesion, "roles").Should().Contain("gestor_impuestos");
        Cadenas(sesion, "permisos").Should().BeEquivalentTo(PermisosGestor, "solo los permisos del producto, no los de plataforma");
        sesion.GetProperty("esSuperAdmin").GetBoolean().Should().BeFalse();
        var aprovisionamiento = sesion.GetProperty("aprovisionamiento");
        aprovisionamiento.GetProperty("estado").GetString().Should().Be("LISTO_SIN_WEBHOOK");
        aprovisionamiento.GetProperty("firmaConfirmada").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Sin_fila_de_aprovisionamiento_el_estado_es_null()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(api.Token(permisos: PermisosGestor)).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var aprovisionamiento = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("aprovisionamiento");
        aprovisionamiento.GetProperty("estado").ValueKind.Should().Be(JsonValueKind.Null, "la compañía todavía no tiene fila de aprovisionamiento");
        aprovisionamiento.GetProperty("firmaConfirmada").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task El_superadmin_ve_todos_los_permisos_del_producto_sin_rol_de_producto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        var respuesta = await api.Cliente(api.Token(roles: [], permisos: [], superAdmin: true, tipoEmpresa: "FLIT")).GetAsync(Ruta, ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, "F5b-CF1: el SA no recibe un rol de producto");
        var sesion = await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);
        sesion.GetProperty("esSuperAdmin").GetBoolean().Should().BeTrue();
        Cadenas(sesion, "permisos").Should().BeEquivalentTo(Permisos.Todos);
    }

    [Fact]
    public async Task Ignora_un_parametro_o_cabecera_con_otra_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        await using (var db = api.Contexto(empresaB))
        {
            db.Add(new AprovisionamientoEmpresa { TenantId = empresaB, Estado = EstadoAprovisionamiento.Listo, FirmaConfirmada = true });
            await db.SaveChangesAsync(ct);
        }

        using var cliente = api.Cliente(api.Token(empresa: empresaA, permisos: PermisosGestor));
        cliente.DefaultRequestHeaders.Add("X-Flit-Tenant-Id", empresaB.ToString());
        var respuesta = await cliente.GetAsync($"{Ruta}?empresa={empresaB}&tenantId={empresaB}", ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        var sesion = JsonElement.Parse(cuerpo);
        sesion.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(empresaA);
        sesion.GetProperty("aprovisionamiento").GetProperty("estado").ValueKind.Should().Be(JsonValueKind.Null, "el aprovisionamiento de B no se filtra a A");
        cuerpo.Should().NotContain(empresaB.ToString());
    }

    [Fact]
    public void Sin_empresa_en_el_token_es_403_SIN_EMPRESA_con_el_literal()
    {
        var usuario = new UsuarioActual(new Microsoft.AspNetCore.Http.HttpContextAccessor(), new EmpresaFija(null));

        var error = FluentActions.Invoking(() => usuario.EmpresaPropia()).Should().Throw<ReglaException>().Which;

        error.Status.Should().Be(403);
        error.Codigo.Should().Be("SIN_EMPRESA");
        error.Message.Should().Be("Tu usuario no tiene una compañía asociada.", "el error es el literal de x-flit-literales.errores");
    }

    private static List<string> Cadenas(JsonElement raiz, string propiedad) =>
        raiz.GetProperty(propiedad).EnumerateArray().Select(e => e.GetString()!).ToList();
}
