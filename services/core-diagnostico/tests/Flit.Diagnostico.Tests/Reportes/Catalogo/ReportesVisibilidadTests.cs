using System.Net;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Configuracion;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (SP-5, SP-6, O9) — los reportes de la compañía respetan la visibilidad «PROPIOS» de <c>AccesoLote</c> (el admin ve
/// todos los lotes) y el filtro de tenant (un SuperAdmin solo ve su compañía; <c>?empresa=</c> da 400). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReportesVisibilidadTests
/// </code>
/// </example>
public sealed class ReportesVisibilidadTests
{
    [Fact]
    public async Task BE2_PROPIOS_y_aislamiento_entre_companias()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var (a, b) = (empresas.A, empresas.B);
        var (u1, u2) = (Guid.NewGuid(), Guid.NewGuid());
        await ConfiguracionOperativaTests.SembrarAsync(a, new ConfiguracionEmpresa { VisibilidadUsuario = VisibilidadUsuario.Propios }, ct);
        await ReportesDePrueba.LoteAsync(a, [new("RPT201", Propietarios: [new("CC", "1111111111")])], ct, creador: u1);
        await ReportesDePrueba.LoteAsync(a, [new("RPT202", Propietarios: [new("CC", "2222222222")])], ct, creador: u2);
        await ReportesDePrueba.LoteAsync(b, [new("RPT203", Propietarios: [new("CC", "3333333333")])], ct);

        async Task<List<string>> PlacasAsync(string token, string tipo = "vehiculos")
        {
            var respuesta = await api.Cliente(token).GetAsync(ReportesDePrueba.Ruta(tipo), ct);
            respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync(ct));
            return [.. ReportesDePrueba.Filas(await ReportesDePrueba.JsonAsync(respuesta, ct)).Select(f => f.GetProperty("placa").GetString()!)];
        }

        (await PlacasAsync(ReportesDePrueba.Token(api, a.Id, u1))).Should().Equal("RPT201");
        (await PlacasAsync(ReportesDePrueba.Token(api, a.Id, u1), "consultas")).Should().Equal("RPT201");
        (await PlacasAsync(ReportesDePrueba.Token(api, a.Id, Guid.NewGuid(), admin: true))).Should().BeEquivalentTo("RPT201", "RPT202");
        var superAdmin = api.Token(empresa: a.Id, permisos: [], roles: ["SuperAdmin"]);
        var delSuperAdmin = await api.Cliente(superAdmin).GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct);
        var cuerpo = await delSuperAdmin.Content.ReadAsStringAsync(ct);
        delSuperAdmin.StatusCode.Should().Be(HttpStatusCode.OK, cuerpo);
        cuerpo.Should().Contain("RPT201").And.Contain("RPT202").And.NotContain("RPT203").And.NotContain("3333333333", "SP-5: el SuperAdmin no se salta el filtro de tenant");

        var conEmpresa = await api.Cliente(ReportesDePrueba.Token(api, a.Id, u1)).GetAsync(ReportesDePrueba.Ruta("vehiculos", $"empresa={b.Id}"), ct);
        conEmpresa.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReportesDePrueba.CodigoAsync(conEmpresa, ct)).Should().Be("FILTRO_INVALIDO");
    }
}
