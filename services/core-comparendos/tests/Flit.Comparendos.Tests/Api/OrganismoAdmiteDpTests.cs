using System.Net.Http.Json;
using System.Text.Json;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Epic #14606, HU #14611 (backend): el organismo admite o no derechos de petición por correo; sobrevive a la réplica del catálogo.</summary>
public sealed class OrganismoAdmiteDpTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private const string Base = "/api/v1/comparendos";

    private sealed class PlataformaFalsa(IReadOnlyList<OrganismoPlataforma> lista) : ICatalogoOrganismosPlataforma
    {
        public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganismoPlataforma>?>(lista);
    }

    private HttpClient Super => api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));

    private async Task<JsonElement> OrganismoAsync(Guid id) =>
        (await Super.GetFromJsonAsync<JsonElement>($"{Base}/organismos?buscar=OT%20ADMITE&tamano=50", TestContext.Current.CancellationToken))
        .GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Por_defecto_no_admite_y_el_superadmin_lo_cambia_sin_tocar_lo_demas()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.OrganismosTransito.Add(new OrganismoTransito { Id = id, Codigo = "99200001", Nombre = "OT ADMITE PRUEBA", CorreosRadicacion = ["radicacion@ot.prueba"] });
            await db.SaveChangesAsync(ct);
        });
        (await OrganismoAsync(id)).GetProperty("admiteDpPorCorreo").GetBoolean().Should().BeFalse();

        var patch = await Super.PatchAsJsonAsync($"{Base}/admin/organismos/{id}", new { admiteDpPorCorreo = true }, ct);
        patch.EnsureSuccessStatusCode();
        (await patch.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("admiteDpPorCorreo").GetBoolean().Should().BeTrue();
        var o = await OrganismoAsync(id);
        o.GetProperty("admiteDpPorCorreo").GetBoolean().Should().BeTrue();
        o.GetProperty("correosRadicacion").EnumerateArray().Select(x => x.GetString()).Should().Equal("radicacion@ot.prueba");

        // Un cambio de otros campos no apaga el interruptor.
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/{id}", new { permiteCambioContraventor = false }, ct)).EnsureSuccessStatusCode();
        (await OrganismoAsync(id)).GetProperty("admiteDpPorCorreo").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Sobrevive_a_la_replica_diaria_del_catalogo()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.OrganismosTransito.Add(new OrganismoTransito { Id = id, Codigo = "99200002", Nombre = "OT ADMITE REPLICA" });
            await db.SaveChangesAsync(ct);
        });
        (await Super.PatchAsJsonAsync($"{Base}/admin/organismos/{id}", new { admiteDpPorCorreo = true }, ct)).EnsureSuccessStatusCode();

        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var lista = (await db.OrganismosTransito.AsNoTracking().ToListAsync(ct))
            .Select(o => new OrganismoPlataforma(o.Id, o.Codigo, o.Id == id ? "OT ADMITE REPLICA RENOMBRADO" : o.Nombre, o.CodigoDivipola, o.Municipio, o.Departamento, o.ActivoPlataforma)).ToList();
        var sincronizacion = new SincronizacionOrganismos(db, new PlataformaFalsa(lista), scope.ServiceProvider.GetRequiredService<Reloj>(), scope.ServiceProvider.GetRequiredService<RemapeoOrganismos>(), NullLogger<SincronizacionOrganismos>.Instance);
        await sincronizacion.SincronizarAsync(ct);

        (await api.EnDbAsync(d => d.OrganismosTransito.AsNoTracking().SingleAsync(o => o.Id == id, ct))).Nombre.Should().Be("OT ADMITE REPLICA RENOMBRADO");
        (await api.EnDbAsync(d => d.OrganismosTransitoDp.AsNoTracking().SingleAsync(o => o.OrganismoTransitoId == id, ct))).AdmiteDpPorCorreo.Should().BeTrue();
    }
}
