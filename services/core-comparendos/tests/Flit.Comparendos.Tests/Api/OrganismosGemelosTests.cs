using System.Net.Http.Json;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Flit.Comparendos.Tests.Api.ComparendosDePrueba;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC punto 3 — organismos que solo difieren en espacios (Restrepo/Meta y Arauca en el catálogo real) cuentan como uno,
/// la sincronización no crea el segundo, y tras sincronizar o crear un alias los comparendos sin organismo se vuelven a
/// mapear con texto y número.
/// </summary>
public sealed class OrganismosGemelosTests(ApiConFuente api) : IClassFixture<ApiConFuente>
{
    private static readonly Guid Restrepo = Guid.Parse("91e3b769-4bc0-582f-94d9-5be94b3c0559");
    private const string TextoDesconocido = "Secretaría que ningún alias conoce";

    private sealed class PlataformaFalsa(IReadOnlyList<OrganismoPlataforma> lista) : ICatalogoOrganismosPlataforma
    {
        public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<OrganismoPlataforma>?>(lista);
    }

    private Task<Dictionary<string, Guid?>> OtsAsync(Guid empresa, CancellationToken ct) =>
        api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa).ToDictionaryAsync(c => c.NumeroNormalizado, c => c.OrganismoTransitoId, ct));

    [Fact]
    public async Task ElMunicipioConGemelos_TieneUnSoloOt_ElTitular()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        await IngresarAsync(api, empresa,
            Item("50606099000012345678") with { Secretaria = TextoDesconocido },
            Item("50606001000012345678") with { Secretaria = TextoDesconocido },
            Item("52110099000012345678") with { Secretaria = TextoDesconocido });

        var ot = await OtsAsync(empresa, ct);
        ot["50606099000012345678"].Should().Be(Restrepo, "Restrepo tiene dos OT que solo difieren en un espacio: son uno");
        ot["50606001000012345678"].Should().Be(Restrepo, "el código del gemelo también lleva al titular");
        ot["52110099000012345678"].Should().BeNull("Buesaco tiene dos OT distintos (municipal y departamental): el municipio no alcanza");
    }

    [Fact]
    public async Task LaSincronizacion_NoCreaElGemelo_YVuelveAMapearLosSinOt()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        var id = (await IngresarAsync(api, empresa, Item("99300000000012345678") with { Secretaria = TextoDesconocido })).Values.Single();
        (await OtsAsync(empresa, ct))["99300000000012345678"].Should().BeNull("el organismo 99300000 aún no está en el catálogo");

        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComparendosDb>();
        var titular = Guid.NewGuid();
        var lista = (await db.OrganismosTransito.AsNoTracking().ToListAsync(ct))
            .Select(o => new OrganismoPlataforma(o.Id, o.Codigo, o.Nombre, o.CodigoDivipola, o.Municipio, o.Departamento, o.ActivoPlataforma))
            .Append(new OrganismoPlataforma(Guid.NewGuid(), "99300001", "SECRETARIA  DE PRUEBA", "99300", "PRUEBA", "DEPTO", true))
            .Append(new OrganismoPlataforma(titular, "99300000", "SECRETARIA DE PRUEBA", "99300", "PRUEBA", "DEPTO", true))
            .ToList();
        var sincronizacion = new SincronizacionOrganismos(db, new PlataformaFalsa(lista), scope.ServiceProvider.GetRequiredService<Reloj>(),
            scope.ServiceProvider.GetRequiredService<RemapeoOrganismos>(), NullLogger<SincronizacionOrganismos>.Instance);

        var r = await sincronizacion.SincronizarAsync(ct);

        r!.Nuevos.Should().Be(1, "el que solo difiere en espacios no se crea");
        (await api.EnDbAsync(d => d.OrganismosTransito.AnyAsync(o => o.Codigo == "99300001", ct))).Should().BeFalse();
        (await api.EnDbAsync(d => d.OrganismosTransito.SingleAsync(o => o.Id == Guid.Parse("dd68d155-3b43-56d4-b93c-aeb18fbaf0da"), ct)))
            .ActivoPlataforma.Should().BeFalse("el gemelo de Restrepo que ya estaba se retira");
        (await OtsAsync(empresa, ct))["99300000000012345678"].Should().Be(titular, "tras sincronizar se vuelve a mapear por número");
        (await api.EnDbAsync(d => d.Timeline.AnyAsync(t => t.ComparendoId == id && t.Tipo == TipoEventoTimeline.DatosActualizados
            && t.Descripcion.Contains("Organismo de tránsito asignado"), ct))).Should().BeTrue();
    }

    [Fact]
    public async Task CrearUnAlias_VuelveAMapearTambienPorNumero()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        await IngresarAsync(api, empresa,
            Item("99400000000012345678") with { Secretaria = "Texto sin organismo número" },
            Item("A0912399") with { Secretaria = "Inspección del alias nuevo" });
        var ot = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.OrganismosTransito.Add(new OrganismoTransito { Id = ot, Codigo = "99400000", Nombre = "OT LLEGADO A MANO" });
            await db.SaveChangesAsync(ct);
        });

        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/organismos/alias",
            new { texto = "inspeccion del alias nuevo", organismoTransitoId = Restrepo }, ct)).EnsureSuccessStatusCode();

        var ots = await OtsAsync(empresa, ct);
        ots["A0912399"].Should().Be(Restrepo, "el texto del alias");
        ots["99400000000012345678"].Should().Be(ot, "el número, aunque su texto no sea el del alias");
    }

    [Fact]
    public async Task LaFuenteCambiaLaSecretaria_SeMapeaTambienPorNumero()
    {
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        api.Reloj.Hoy(new DateOnly(2026, 10, 9));
        var empresa = await EmpresaAsync(api);
        await IngresarAsync(api, empresa, Item("99500000000012345678") with { Secretaria = "Primer texto desconocido" });
        var ot = Guid.NewGuid();
        await api.EnDbAsync(async db =>
        {
            db.OrganismosTransito.Add(new OrganismoTransito { Id = ot, Codigo = "99500000", Nombre = "OT DE LA ACTUALIZACION" });
            await db.SaveChangesAsync(ct);
        });

        await IngresarAsync(api, empresa, Item("99500000000012345678") with { Secretaria = "Segundo texto desconocido" });

        (await OtsAsync(empresa, ct))["99500000000012345678"].Should().Be(ot, "al cambiar el texto se mapea con texto y número, como al ingresar");
    }
}
