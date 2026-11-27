using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Usuarios;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Configuracion;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M13, F6-CF2, F5b-CF1) — los roles y permisos de otro producto no abren Impuestos ni valen dentro de él: un
/// administrador de Diagnóstico recibe 403 en <c>/usuarios</c>; un rol de otro producto (o el genérico <c>usuario_impuestos</c>)
/// no se acepta como filtro, al invitar ni al cambiar rol; y una fila que la plataforma devolviera con un rol ajeno no se expone.
/// Los roles asignables son exactamente los del manifiesto (<see cref="Permisos.RolesPorDefecto"/>) y los del enum
/// <c>RolImpuestos</c> del contrato.
/// </summary>
/// <example>
/// <code>
/// RolesImpuestos.Todos.Should().Equal("gestor_impuestos", "admin_impuestos");
/// </code>
/// </example>
public sealed class RolesDeOtroProductoTests(ApiUsuariosFixture fixture) : IClassFixture<ApiUsuariosFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public void Los_roles_de_Impuestos_son_los_del_manifiesto_y_los_del_contrato()
    {
        RolesImpuestos.Todos.Should().Equal("gestor_impuestos", "admin_impuestos");
        Permisos.RolesPorDefecto.Select(r => r.Codigo).Should().BeEquivalentTo(RolesImpuestos.Todos);
        RolesImpuestos.Gestor.Should().Be("gestor_impuestos");
        RolesImpuestos.Administrador.Should().Be("admin_impuestos");
    }

    [Fact]
    public async Task Un_administrador_de_Diagnostico_recibe_403_en_usuarios_de_Impuestos()
    {
        var ct = Preparar();
        var token = Api.Token(roles: ["admin_diagnostico"], permisos: ["diagnostico.usuarios.gestionar", "diagnostico.dashboard.ver"]);

        using var r = await Api.Cliente(token).GetAsync(UsuariosEndpointsTests.Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Plataforma.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("admin_diagnostico")]
    [InlineData("usuario_diagnostico")]
    [InlineData("usuario_impuestos")]
    [InlineData("AdminCompany")]
    public async Task Un_rol_de_otro_producto_no_es_filtro_ni_se_invita_ni_se_asigna(string rol)
    {
        var ct = Preparar();
        var admin = Api.Cliente(UsuariosEndpointsTests.TokenAdmin(Api, Guid.NewGuid()));

        using var filtro = await admin.GetAsync($"{UsuariosEndpointsTests.Ruta}?rol={rol}", ct);
        using var invitar = await admin.PostAsJsonAsync(UsuariosEndpointsTests.Ruta, new { email = "x@empresa.test", nombre = "X", rol }, ct);
        using var cambiar = await admin.PutAsJsonAsync($"{UsuariosEndpointsTests.Ruta}/{Guid.NewGuid()}/rol", new { rol }, ct);

        await ConfiguracionTests.DebeSerRegla(filtro, HttpStatusCode.BadRequest, "FILTRO_INVALIDO", "Un filtro no es válido.", null, ct);
        await ConfiguracionTests.DebeSerRegla(invitar, HttpStatusCode.UnprocessableEntity, "ROL_NO_PERMITIDO", "El rol no es válido para Impuestos.", "rol", ct);
        await ConfiguracionTests.DebeSerRegla(cambiar, HttpStatusCode.UnprocessableEntity, "ROL_NO_PERMITIDO", "El rol no es válido para Impuestos.", "rol", ct);
        fixture.Plataforma.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_fila_con_rol_de_otro_producto_no_se_expone()
    {
        var ct = Preparar();
        var (propia, ajena) = (Guid.NewGuid(), Guid.NewGuid());
        fixture.Plataforma.Filas =
        [
            UsuariosPlataformaDePrueba.Fila(propia, "Ana", "ana@empresa.test", "gestor_impuestos"),
            UsuariosPlataformaDePrueba.Fila(ajena, "Beto", "beto@empresa.test", "usuario_diagnostico"),
        ];

        using var r = await Api.Cliente(UsuariosEndpointsTests.TokenAdmin(Api, Guid.NewGuid())).GetAsync(UsuariosEndpointsTests.Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        json.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(propia);
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        fixture.Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}
