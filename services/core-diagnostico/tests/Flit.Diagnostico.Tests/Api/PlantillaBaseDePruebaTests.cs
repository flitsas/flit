using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Api;

/// <summary>
/// Plantilla de base migrada una vez para las pruebas: <see cref="ApiDePrueba.ConBaseAsync"/> copia cada base efímera de
/// <c>flit_dgn_tpl_&lt;huella&gt;</c> en vez de aplicar las migraciones en cada prueba. La huella sale de la lista de migraciones
/// del ensamblado: una migración nueva genera otra plantilla. Las pruebas no se conectan a la plantilla (Postgres no copia una
/// base con sesiones abiertas); la consultan desde <c>pg_database</c>.
/// </summary>
/// <example>
/// <code>
/// var huella = PlantillaBaseDePrueba.Huella(["20261020_D0", "20261021_D1"]); // 10 hex
/// await using var api = await ApiDePrueba.ConBaseAsync(); // api.Plantilla == "flit_dgn_tpl_" + huella actual
/// </code>
/// </example>
public sealed class PlantillaBaseDePruebaTests
{
    [Fact]
    public void Huella_cambia_si_cambia_la_lista_de_migraciones()
    {
        var base2 = PlantillaBaseDePrueba.Huella(["20261020000000_D0", "20261021000000_D1"]);

        base2.Should().MatchRegex("^[0-9a-f]{10}$");
        PlantillaBaseDePrueba.Huella(["20261020000000_D0", "20261021000000_D1"]).Should().Be(base2, "es determinista");
        PlantillaBaseDePrueba.Huella(["20261021000000_D1", "20261020000000_D0"]).Should().Be(base2, "la lista se ordena");
        PlantillaBaseDePrueba.Huella(["20261020000000_D0", "20261021000000_D1", "20261022000000_D2"]).Should().NotBe(base2, "una migración nueva cambia la huella");
        PlantillaBaseDePrueba.Huella(["20261020000000_D0"]).Should().NotBe(base2, "quitar una migración cambia la huella");
        PlantillaBaseDePrueba.Nombre(base2).Should().Be("flit_dgn_tpl_" + base2);
    }

    [Fact]
    public void Huella_actual_sale_de_las_migraciones_del_ensamblado()
    {
        using var db = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", null);

        PlantillaBaseDePrueba.HuellaActual.Should().Be(PlantillaBaseDePrueba.Huella(db.Database.GetMigrations()));
    }

    [Fact]
    public async Task Dos_bases_seguidas_reutilizan_la_plantilla_sin_volver_a_migrarla()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var primera = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(primera.Omitir is not null, primera.Omitir ?? string.Empty);
        var creaciones = PlantillaBaseDePrueba.Creaciones;

        await using var segunda = await ApiDePrueba.ConBaseAsync();

        var esperada = PlantillaBaseDePrueba.Nombre(PlantillaBaseDePrueba.HuellaActual);
        primera.Plantilla.Should().Be(esperada);
        segunda.Plantilla.Should().Be(esperada);
        PlantillaBaseDePrueba.Creaciones.Should().Be(creaciones, "la segunda base se copia de la plantilla ya creada");
        creaciones.Should().BeLessThanOrEqualTo(1, "la plantilla se migra a lo sumo una vez por proceso");
        (await BasesAsync(primera, "flit\\_dgn\\_tpl\\_%", ct)).Should().Contain(esperada, "la plantilla de la huella actual existe en el servidor");
    }

    [Fact]
    public async Task La_base_copiada_tiene_todas_las_migraciones_aplicadas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        api.Plantilla.Should().NotBeNull("la base se copia de la plantilla");

        using var modelo = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", null);
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var historial = new NpgsqlCommand("SELECT migration_id FROM diagnostico.\"__EFMigrationsHistory\" ORDER BY migration_id", conexion);
        var aplicadas = new List<string>();
        await using (var lector = await historial.ExecuteReaderAsync(ct))
        {
            while (await lector.ReadAsync(ct))
                aplicadas.Add(lector.GetString(0));
        }

        aplicadas.Should().Equal(modelo.Database.GetMigrations().OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Lo_escrito_en_una_base_no_aparece_en_otra()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var a = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(a.Omitir is not null, a.Omitir ?? string.Empty);
        await using var b = await ApiDePrueba.ConBaseAsync();
        a.Plantilla.Should().NotBeNull();
        b.Plantilla.Should().Be(a.Plantilla);

        await using (var conexionA = new NpgsqlConnection(a.BaseDeDatos))
        {
            await conexionA.OpenAsync(ct);
            await using var escribir = new NpgsqlCommand("CREATE TABLE public.marca_aislamiento_plantilla (id int); INSERT INTO public.marca_aislamiento_plantilla VALUES (1)", conexionA);
            await escribir.ExecuteNonQueryAsync(ct);
        }

        await using var conexionB = new NpgsqlConnection(b.BaseDeDatos);
        await conexionB.OpenAsync(ct);
        await using var existe = new NpgsqlCommand("SELECT to_regclass('public.marca_aislamiento_plantilla') IS NOT NULL", conexionB);
        ((bool)(await existe.ExecuteScalarAsync(ct))!).Should().BeFalse("cada prueba tiene su propia copia de la plantilla");
    }

    private static async Task<List<string>> BasesAsync(ApiDePrueba api, string patron, CancellationToken ct)
    {
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(api.BaseDeDatos) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync(ct);
        await using var consulta = new NpgsqlCommand("SELECT datname FROM pg_database WHERE datname LIKE @patron ORDER BY datname", admin);
        consulta.Parameters.AddWithValue("patron", patron);
        var bases = new List<string>();
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            bases.Add(lector.GetString(0));
        return bases;
    }
}
