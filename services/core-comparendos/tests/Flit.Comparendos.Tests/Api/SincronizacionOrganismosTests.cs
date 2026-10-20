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

/// <summary>HU #13459 (F7, S-66) — la réplica diaria del catálogo de organismos de la plataforma.</summary>
public sealed class SincronizacionOrganismosTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private sealed class PlataformaFalsa(IReadOnlyList<OrganismoPlataforma>? lista) : ICatalogoOrganismosPlataforma
    {
        public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult(lista);
    }

    [Fact]
    public async Task F7_CreaActualizaYRetira_SinTocarLoQueEsDeComparendos()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var cambia = Guid.NewGuid();
        var sale = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.OrganismosTransito.AddRange(
                new OrganismoTransito { Id = cambia, Codigo = "99100001", Nombre = "OT DE PRUEBA", Municipio = "VIEJO", CorreosRadicacion = ["radicacion@ot.prueba"], PermiteCambioContraventor = false },
                new OrganismoTransito { Id = sale, Codigo = "99100002", Nombre = "OT QUE SALE DE LA PLATAFORMA" });
            await db.SaveChangesAsync(ct);
        });

        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        // La plataforma devuelve todo lo de hoy salvo «sale», con «cambia» renombrado e inactivo, y un OT nuevo.
        var nuevo = Guid.NewGuid();
        var lista = (await db.OrganismosTransito.AsNoTracking().Where(o => o.Id != sale && o.Id != cambia).ToListAsync(ct))
            .Select(o => new OrganismoPlataforma(o.Id, o.Codigo, o.Nombre, o.CodigoDivipola, o.Municipio, o.Departamento, o.ActivoPlataforma))
            .Append(new OrganismoPlataforma(cambia, "99100001", "OT DE PRUEBA RENOMBRADO", "99100", "NUEVO", "DEPTO", false))
            .Append(new OrganismoPlataforma(nuevo, "99100003", "OT NUEVO EN LA PLATAFORMA", "99100", "NUEVO", "DEPTO", true))
            .ToList();
        var sincronizacion = new SincronizacionOrganismos(db, new PlataformaFalsa(lista), scope.ServiceProvider.GetRequiredService<Reloj>(), NullLogger<SincronizacionOrganismos>.Instance);

        var r = await sincronizacion.SincronizarAsync(ct);

        (r!.Nuevos, r.Actualizados, r.Retirados).Should().Be((1, 1, 1));
        var ots = await api.EnDbAsync(d => d.OrganismosTransito.AsNoTracking().Where(o => o.Id == cambia || o.Id == sale || o.Id == nuevo).ToDictionaryAsync(o => o.Id, ct));
        (ots[cambia].Nombre, ots[cambia].Municipio, ots[cambia].ActivoPlataforma).Should().Be(("OT DE PRUEBA RENOMBRADO", "NUEVO", false));
        ots[cambia].CorreosRadicacion.Should().Equal(["radicacion@ot.prueba"], "los correos de radicación son de Comparendos (S-66)");
        ots[cambia].PermiteCambioContraventor.Should().BeFalse("el permiso de cambio de contraventor también es de Comparendos");
        ots[sale].ActivoPlataforma.Should().BeFalse("la plataforma ya no lo trae");
        (ots[nuevo].Codigo, ots[nuevo].ActivoPlataforma).Should().Be(("99100003", true));
    }

    [Fact]
    public async Task F7_SinConexionOListaVacia_NoTocaNada()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var reloj = scope.ServiceProvider.GetRequiredService<Reloj>();
        var activos = await db.OrganismosTransito.CountAsync(o => o.ActivoPlataforma, ct);

        (await new SincronizacionOrganismos(db, new PlataformaFalsa(null), reloj, NullLogger<SincronizacionOrganismos>.Instance).SincronizarAsync(ct)).Should().BeNull();
        (await new SincronizacionOrganismos(db, new PlataformaFalsa([]), reloj, NullLogger<SincronizacionOrganismos>.Instance).SincronizarAsync(ct))
            .Should().BeNull("una lista vacía sería borrar el catálogo");
        (await db.OrganismosTransito.CountAsync(o => o.ActivoPlataforma, ct)).Should().Be(activos);
    }
}
