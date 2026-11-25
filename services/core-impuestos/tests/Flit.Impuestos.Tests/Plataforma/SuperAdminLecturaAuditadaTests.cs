using System.Net;
using System.Net.Http.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Credenciales;
using Flit.Impuestos.Tests.Usuarios;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Plataforma;

/// <summary>
/// HU #14694 (M13, plan §5.1, forma de ADR-0073) — cada lectura del Super Admin sobre otra compañía deja una fila en
/// <c>impuestos.auditoria_lectura_backoffice</c> (actor = <c>sub</c>, compañía consultada, recurso) antes de leer: los usuarios con
/// <c>?empresa</c>, la lista del backoffice (una fila por compañía devuelta) y el detalle del aprovisionamiento. Las escrituras y
/// las peticiones rechazadas por la regla de empresa no dejan lectura auditada; el Administrador sobre su compañía tampoco.
/// </summary>
/// <example>
/// <code>
/// await using var db = api.Contexto(empresa);
/// var lecturas = await db.Set&lt;LecturaBackoffice&gt;().ToListAsync(ct);
/// </code>
/// </example>
public sealed class SuperAdminLecturaAuditadaTests(ApiUsuariosFixture fixture) : IClassFixture<ApiUsuariosFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Leer_usuarios_de_otra_compania_queda_auditado_con_el_actor_y_el_recurso()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Listo);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync($"{UsuariosEndpointsTests.Ruta}?empresa={empresa}", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        var lectura = (await LecturasAsync(empresa, ct)).Should().ContainSingle().Subject;
        lectura.ActorId.Should().Be(CargaManualCredencialesTests.ActorSuperAdmin);
        lectura.Recurso.Should().Be("usuarios");
    }

    [Fact]
    public async Task La_lista_del_backoffice_audita_una_lectura_por_compania_devuelta()
    {
        var ct = Preparar();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var marca = Guid.NewGuid().ToString("N")[..8];
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, a, ct, EstadoAprovisionamiento.Listo);
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, b, ct, EstadoAprovisionamiento.Suspendido);
        fixture.Directorio.Sembrar(a, $"Uno {marca}", null);
        fixture.Directorio.Sembrar(b, $"Dos {marca}", null);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync($"{SuspensionConservaDatosTests.RutaEmpresas}?q={marca}", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await LecturasAsync(a, ct)).Should().ContainSingle().Which.Recurso.Should().Be("empresas");
        (await LecturasAsync(b, ct)).Should().ContainSingle().Which.Recurso.Should().Be("empresas");
    }

    [Fact]
    public async Task El_detalle_del_aprovisionamiento_queda_auditado()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Error);

        using var r = await Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api)).GetAsync($"{SuspensionConservaDatosTests.RutaEmpresas}/{empresa}/aprovisionamiento", ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        (await LecturasAsync(empresa, ct)).Should().ContainSingle().Which.Recurso.Should().Be("aprovisionamiento");
    }

    [Fact]
    public async Task Escrituras_rechazos_y_el_Administrador_en_su_compania_no_dejan_lectura_auditada()
    {
        var ct = Preparar();
        var empresa = Guid.NewGuid();
        await AlmacenCredencialesTests.SembrarAprovisionamientoAsync(Api, empresa, ct, EstadoAprovisionamiento.Listo);
        var sa = Api.Cliente(CargaManualCredencialesTests.TokenSuperAdmin(Api));

        using var suspender = await sa.PostAsync($"{UsuariosEndpointsTests.Ruta}/{Guid.NewGuid()}/suspender?empresa={empresa}", null, ct);
        using var invitar = await sa.PostAsJsonAsync($"{UsuariosEndpointsTests.Ruta}?empresa={empresa}", new { email = "x@empresa.test", nombre = "X", rol = "admin_impuestos" }, ct);
        using var repetida = await sa.GetAsync($"{UsuariosEndpointsTests.Ruta}?empresa={empresa}&empresa={Guid.NewGuid()}", ct);
        using var admin = await Api.Cliente(UsuariosEndpointsTests.TokenAdmin(Api, empresa)).GetAsync(UsuariosEndpointsTests.Ruta, ct);

        suspender.StatusCode.Should().Be(HttpStatusCode.NoContent);
        invitar.StatusCode.Should().Be(HttpStatusCode.Created, await invitar.Content.ReadAsStringAsync(ct));
        repetida.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LecturasAsync(empresa, ct)).Should().BeEmpty();
    }

    private async Task<List<LecturaBackoffice>> LecturasAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = Api.Contexto(empresa);
        return await db.Set<LecturaBackoffice>().AsNoTracking().ToListAsync(ct);
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        fixture.Plataforma.Reiniciar();
        return TestContext.Current.CancellationToken;
    }
}
