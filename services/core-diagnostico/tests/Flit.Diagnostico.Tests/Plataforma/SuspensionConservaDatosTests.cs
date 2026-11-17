using System.Net;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Plataforma;

/// <summary>
/// HU #13726 (F00.1-H4), escenario 6 — suspender y reactivar Diagnóstico no borra ni desvincula datos: el guard solo
/// niega la petición; no hay camino de borrado. Contra un Postgres real (base efímera de <see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// ConnectionStrings__Core="Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres" \
///   dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~SuspensionConservaDatosTests
/// </code>
/// </example>
public sealed class SuspensionConservaDatosTests
{
    // Escenario 6 — filas de A en el schema diagnostico; se suspende y se reactiva A; las filas siguen intactas y de A.
    [Fact]
    public async Task Suspender_y_reactivar_conserva_las_filas_de_la_compania()
    {
        await using var api = await ApiDePrueba.ConBaseAsync(EscenariosDeFuga.MapearSondas);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var a = DosEmpresas.En(api).A;
        var filas = new[]
        {
            new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/suspension/1" },
            new LecturaBackoffice { EmpresaConsultada = Guid.NewGuid(), UsuarioId = Guid.NewGuid(), Ruta = "/suspension/2" },
        };
        await using (var db = a.Db())
        {
            db.Set<LecturaBackoffice>().AddRange(filas);
            await db.SaveChangesAsync(ct);
        }

        var antes = await Instantanea(api, ct);
        api.Productos.Apagar(a.Id);
        (await a.Cliente().GetAsync(EscenariosDeFuga.RutaSonda, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "suspendida");
        (await Instantanea(api, ct)).Should().BeEquivalentTo(antes, "suspender no borra ni desvincula");

        api.Productos.Encender(a.Id);
        var reactivada = await a.Cliente().GetAsync(EscenariosDeFuga.RutaSonda, ct);

        reactivada.StatusCode.Should().Be(HttpStatusCode.OK, "reactivada");
        (await reactivada.Content.ReadAsStringAsync(ct)).Should().ContainAll(filas.Select(f => f.Id.ToString()), "A vuelve a ver sus filas");
        var despues = await Instantanea(api, ct);
        despues.Should().BeEquivalentTo(antes, "reactivar no recrea ni reasigna");
        despues.Should().OnlyContain(f => f.TenantId == a.Id, "siguen asociadas a A");
    }

    /// <summary>Todas las filas de la tabla, sin filtro de compañía (contexto de SuperAdmin), para comparar antes y después.</summary>
    private static async Task<List<(Guid Id, Guid TenantId, string Ruta)>> Instantanea(ApiDePrueba api, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa: null);
        var filas = await db.Set<LecturaBackoffice>().IgnoreQueryFilters().AsNoTracking()
            .OrderBy(l => l.Id).Select(l => new { l.Id, l.TenantId, l.Ruta }).ToListAsync(ct);
        return filas.Select(f => (f.Id, f.TenantId, f.Ruta)).ToList();
    }
}
