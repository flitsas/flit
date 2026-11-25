using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Tramites.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Catalogos;

/// <summary>
/// HU #14694 (M08, F2a-CF3, LP-03; AC-BE-30 y AC-BE-31) — homologación de la secretaría que reporta el RUNT a
/// <c>secretaryDepartmentCode</c> (DANE, 2 cifras) y <c>secretaryCity</c> con la réplica propia de organismos
/// (<c>impuestos.organismos</c>), que se sincroniza por gRPC con <c>CatalogosService.ListarOrganismosTransito</c> de core-api. Por
/// código si viene y existe; si no, por nombre normalizado (mayúsculas, sin tildes, un espacio). Organismo ausente, ambiguo (varios
/// departamentos o ciudades) o sin departamento o ciudad: null (el ítem queda «Datos incompletos», M09).
/// </summary>
/// <example>
/// <code>
/// var secretaria = await homologacion.HomologarAsync(codigo: null, nombre: vehiculo.OrganismoTransito, ct);
/// // secretaria?.DepartamentoCodigo == "05", secretaria?.Ciudad == "Medellín"
/// </code>
/// </example>
public sealed class HomologacionSecretariaTests(HomologacionFixture fixture) : IClassFixture<HomologacionFixture>
{
    [Theory]
    [InlineData("  Secretaría de  Tránsito ", "SECRETARIA DE TRANSITO")]
    [InlineData("stría tto. Medellín", "STRIA TTO. MEDELLIN")]
    [InlineData("Ñuñoa ü", "NUNOA U")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalizar_quita_tildes_espacios_y_pasa_a_mayusculas(string? nombre, string esperado) =>
        HomologacionSecretaria.Normalizar(nombre).Should().Be(esperado);

    [Fact]
    public async Task Un_organismo_presente_por_nombre_da_el_DANE_y_la_ciudad()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);
        var sufijo = Sufijo();
        await SembrarAsync(fixture.Api, ct, ("05001" + sufijo[..3], $"Secretaría de Movilidad de Medellín {sufijo}", "05", "Antioquia", "Medellín"));

        var r = await Homologar(fixture.Api, null, $"  SECRETARIA DE MOVILIDAD   DE MEDELLIN {sufijo.ToLowerInvariant()}", ct);

        r.Should().Be(new SecretariaHomologada("05", "Medellín"));
    }

    [Fact]
    public async Task Por_codigo_gana_el_codigo_aunque_el_nombre_no_coincida()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);
        var sufijo = Sufijo();
        var codigo = "C" + sufijo[..8];
        await SembrarAsync(fixture.Api, ct, (codigo, $"Tránsito de Chía {sufijo}", "25", "Cundinamarca", "Chía"));

        (await Homologar(fixture.Api, codigo, "otro nombre", ct)).Should().Be(new SecretariaHomologada("25", "Chía"));
        (await Homologar(fixture.Api, "NO-EXISTE-" + sufijo[..4], $"Tránsito de Chía {sufijo}", ct))
            .Should().Be(new SecretariaHomologada("25", "Chía"), "un código desconocido cae al nombre");
    }

    [Fact]
    public async Task Un_nombre_de_organismos_de_departamentos_distintos_es_ambiguo_y_da_null()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);
        var sufijo = Sufijo();
        await SembrarAsync(fixture.Api, ct,
            ("A" + sufijo[..8], $"Secretaría de Tránsito Municipal {sufijo}", "05", "Antioquia", "Envigado"),
            ("B" + sufijo[..8], $"SECRETARIA DE TRANSITO MUNICIPAL {sufijo}", "68", "Santander", "Girón"));

        (await Homologar(fixture.Api, null, $"Secretaría de Tránsito Municipal {sufijo}", ct)).Should().BeNull();
    }

    [Fact]
    public async Task El_mismo_nombre_en_el_mismo_departamento_y_ciudad_no_es_ambiguo()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);
        var sufijo = Sufijo();
        await SembrarAsync(fixture.Api, ct,
            ("D" + sufijo[..8], $"Tránsito Bello {sufijo}", "05", "Antioquia", "Bello"),
            ("E" + sufijo[..8], $"Transito Bello {sufijo}", "05", "Antioquia", "Bello"));

        (await Homologar(fixture.Api, null, $"Tránsito Bello {sufijo}", ct)).Should().Be(new SecretariaHomologada("05", "Bello"));
    }

    [Fact]
    public async Task Un_organismo_ausente_o_sin_nombre_da_null()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);

        (await Homologar(fixture.Api, null, $"No existe {Sufijo()}", ct)).Should().BeNull();
        (await Homologar(fixture.Api, null, "   ", ct)).Should().BeNull();
        (await Homologar(fixture.Api, null, null, ct)).Should().BeNull();
    }

    [Fact]
    public async Task Un_organismo_sin_departamento_o_sin_ciudad_da_null()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(fixture.Api.Omitir is not null, fixture.Api.Omitir ?? string.Empty);
        var sufijo = Sufijo();
        await SembrarAsync(fixture.Api, ct,
            ("F" + sufijo[..8], $"Sin ciudad {sufijo}", "05", "Antioquia", null),
            ("G" + sufijo[..8], $"Sin departamento {sufijo}", null, null, "Neiva"));

        (await Homologar(fixture.Api, null, $"Sin ciudad {sufijo}", ct)).Should().BeNull();
        (await Homologar(fixture.Api, "G" + sufijo[..8], null, ct)).Should().BeNull();
    }

    [Fact]
    public async Task La_sincronizacion_crea_actualiza_retira_y_completa_el_codigo_de_departamento()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalogo = new CatalogoOrganismosFijo();
        await using var api = await ApiDePrueba.ConBaseAsync(b => b.Services.AddSingleton<ICatalogoOrganismosPlataforma>(catalogo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await SembrarAsync(api, ct, ("VIEJO", "Organismo retirado", "05", "Antioquia", "Medellín"), ("05001000", "Nombre anterior", "05", "Antioquia", "Medellín"));
        catalogo.Organismos =
        [
            new OrganismoPlataforma(" 05001000 ", " Secretaría de Movilidad de Medellín ", "05", "Antioquia", "Medellín"),
            new OrganismoPlataforma("8001000", "Tránsito de Barranquilla", "8", "Atlántico", "Barranquilla"),
            new OrganismoPlataforma("99999000", "Departamento raro", "123", null, "  "),
            new OrganismoPlataforma("8001000", "Repetido", "08", null, null),
            new OrganismoPlataforma("   ", "Sin código", "05", null, null),
        ];

        ResultadoSincronizacionOrganismos? resultado;
        using (var scope = api.App.Services.CreateScope())
            resultado = await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct);

        resultado.Should().Be(new ResultadoSincronizacionOrganismos(Total: 5, Nuevos: 2, Actualizados: 1, Retirados: 1, Omitidos: 2));
        await using var db = api.Contexto(null);
        var filas = await db.Set<Organismo>().AsNoTracking().OrderBy(o => o.Codigo).ToListAsync(ct);
        filas.Select(o => o.Codigo).Should().Equal("05001000", "8001000", "99999000");
        filas[0].Nombre.Should().Be("Secretaría de Movilidad de Medellín");
        filas[0].NombreNormalizado.Should().Be("SECRETARIA DE MOVILIDAD DE MEDELLIN");
        filas[1].DepartamentoCodigo.Should().Be("08", "un departamento de una cifra perdió el cero inicial");
        filas[2].DepartamentoCodigo.Should().BeNull("un código de departamento que no es de 2 cifras no se guarda");
        filas[2].Municipio.Should().BeNull();
        filas.Should().OnlyContain(o => o.SincronizadoEn > DateTimeOffset.UtcNow.AddMinutes(-5));

        using var scope2 = api.App.Services.CreateScope();
        (await scope2.ServiceProvider.GetRequiredService<HomologacionSecretaria>().HomologarAsync(null, "transito de barranquilla", ct))
            .Should().Be(new SecretariaHomologada("08", "Barranquilla"), "la réplica sincronizada alimenta la homologación");
    }

    [Fact]
    public async Task Una_lista_vacia_o_sin_conexion_no_toca_la_replica()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalogo = new CatalogoOrganismosFijo { Organismos = [] };
        await using var api = await ApiDePrueba.ConBaseAsync(b => b.Services.AddSingleton<ICatalogoOrganismosPlataforma>(catalogo));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await SembrarAsync(api, ct, ("05001000", "Medellín", "05", "Antioquia", "Medellín"));

        using (var scope = api.App.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct)).Should().BeNull();
        catalogo.Organismos = null;
        using (var scope = api.App.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<SincronizacionOrganismos>().SincronizarAsync(ct)).Should().BeNull();

        await using var db = api.Contexto(null);
        (await db.Set<Organismo>().CountAsync(ct)).Should().Be(1);
        (await new CatalogoOrganismosNoConectado().ListarAsync(ct)).Should().BeNull();
    }

    [Fact]
    public async Task El_cliente_gRPC_descarta_los_organismos_sin_codigo_y_deja_null_lo_vacio()
    {
        var ct = TestContext.Current.CancellationToken;
        var respuesta = new ListarOrganismosTransitoResponse();
        respuesta.Organismos.Add(new OrganismoTransito { Codigo = " 5001000 ", Nombre = " Medellín ", DepartamentoCodigo = "05", Departamento = "Antioquia", Municipio = "Medellín", Activo = true });
        respuesta.Organismos.Add(new OrganismoTransito { Codigo = "", Nombre = "Sin código" });
        respuesta.Organismos.Add(new OrganismoTransito { Codigo = "11001000", Nombre = "Bogotá", DepartamentoCodigo = " ", Municipio = "" });

        var organismos = await new CatalogoOrganismosGrpc(new CatalogosFalso(respuesta)).ListarAsync(ct);

        organismos.Should().Equal(
            new OrganismoPlataforma("5001000", "Medellín", "05", "Antioquia", "Medellín"),
            new OrganismoPlataforma("11001000", "Bogotá", null, null, null));
    }

    private static string Sufijo() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static async Task<SecretariaHomologada?> Homologar(ApiDePrueba api, string? codigo, string? nombre, CancellationToken ct)
    {
        using var scope = api.App.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HomologacionSecretaria>().HomologarAsync(codigo, nombre, ct);
    }

    internal static async Task SembrarAsync(
        ApiDePrueba api, CancellationToken ct, params (string Codigo, string Nombre, string? DepartamentoCodigo, string? Departamento, string? Municipio)[] organismos)
    {
        await using var db = api.Contexto(null);
        foreach (var o in organismos)
        {
            db.Add(new Organismo
            {
                Codigo = o.Codigo,
                Nombre = o.Nombre,
                NombreNormalizado = HomologacionSecretaria.Normalizar(o.Nombre),
                DepartamentoCodigo = o.DepartamentoCodigo,
                Departamento = o.Departamento,
                Municipio = o.Municipio,
                SincronizadoEn = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private sealed class CatalogoOrganismosFijo : ICatalogoOrganismosPlataforma
    {
        public IReadOnlyList<OrganismoPlataforma>? Organismos { get; set; }

        public Task<IReadOnlyList<OrganismoPlataforma>?> ListarAsync(CancellationToken ct) => Task.FromResult(Organismos);
    }

    private sealed class CatalogosFalso(ListarOrganismosTransitoResponse respuesta) : CatalogosService.CatalogosServiceClient
    {
        public override AsyncUnaryCall<ListarOrganismosTransitoResponse> ListarOrganismosTransitoAsync(
            ListarOrganismosTransitoRequest request, CallOptions options) =>
            new(Task.FromResult(respuesta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }
}

/// <summary>Una API con base para las pruebas de homologación (cada una siembra organismos con nombres únicos).</summary>
public sealed class HomologacionFixture : IAsyncLifetime
{
    public ApiDePrueba Api { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Api = await ApiDePrueba.ConBaseAsync();

    public async ValueTask DisposeAsync() => await Api.DisposeAsync();
}
