using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12371 (Feature #12150) — el radicado pasa de número pelado a <c>FT1-0000012</c>.
/// <para>
/// Como en <see cref="ConsecutivoGlobalTramiteTests"/>: la tabla está <c>ExcludeFromMigrations</c> y la
/// suite no tiene Postgres, así que aquí se verifica que el DDL <i>diga</i> lo que debe y que el modelo
/// de EF esté declarado como debe. Que el motor lo ejecute (trigger, inmutabilidad, orden y búsqueda
/// sobre SQL real, seeds reejecutados, <c>Down</c> y re-<c>Up</c>) se comprobó contra una copia de
/// la base de dev; queda en las evidencias de la HU.
/// </para>
/// <para>
/// La prueba que más vale es <see cref="ElMapaDeFamiliasDelDdlEsElDelDominio"/>: el prefijo vive en
/// dos sitios a la fuerza —el <c>CASE</c> del trigger y <see cref="Radicado.Prefijo"/>— y esta es la
/// que impide que se separen cuando aparezca una cuarta familia.
/// </para>
/// </summary>
public sealed class RadicadoPrefijoFamiliaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.108-HU12371-radicado-prefijo-familia.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Quita los comentarios de línea SQL para no medir lo que dice la documentación.</summary>
    private static string SinComentarios(string sql) =>
        string.Join('\n', sql.Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    // ── DDL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ElMapaDeFamiliasDelDdlEsElDelDominio()
    {
        // El CASE del script está alineado con espacios; se colapsan para comparar por contenido.
        var sql = System.Text.RegularExpressions.Regex.Replace(SinComentarios(LoadDdl()), @"\s+", " ");

        foreach (var familia in Enum.GetValues<ProcedureFamily>())
        {
            var codigo = ProcedureFamilyCodes.ToCode(familia);
            var prefijo = Radicado.Prefijo(familia);

            // OTROS es el ELSE del CASE (igual que FromCodeOrOtros degrada a Otros); las demás
            // familias tienen que aparecer con SU código y SU prefijo, en el backfill y en el trigger.
            var esperado = familia == ProcedureFamily.Otros
                ? $"ELSE '{prefijo}'"
                : $"WHEN '{codigo}' THEN '{prefijo}'";

            var apariciones = sql.Split(esperado).Length - 1;
            apariciones.Should().Be(2, $"{familia} debe mapearse a {prefijo} en el backfill y en el trigger");
        }
    }

    [Fact]
    public void ElAnchoEsMinimoYNoTope()
    {
        // lpad TRUNCA cuando el texto es más largo que el ancho: lpad('12345678', 7, '0') = '1234567'.
        // La primera versión del trigger recortaba los rangos sintéticos de los seeds a FT1-9100000;
        // lo atrapó Postgres real. greatest(7, length(...)) es lo que hace que el número gane un
        // dígito en vez de perderlo (AC3).
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("lpad(p.consecutivo::text, greatest(7, length(p.consecutivo::text)), '0')");
        sql.Should().Contain("lpad(NEW.consecutivo::text, greatest(7, length(NEW.consecutivo::text)), '0')");
        sql.Should().NotContain("::text, 7, '0')", "un lpad a ancho fijo recorta");
    }

    [Fact]
    public void ElCheckDelDdlEsElPatronDelDominio()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain($"CHECK (reference_number ~ '{Radicado.PatronSql}')");
        sql.Should().Contain("DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_numerico");
        sql.Should().Contain("ck_procedure_instances_reference_formato");
        sql.Should().Contain("CHECK (consecutivo > 0)");
    }

    [Fact]
    public void LaBaseComponeAlInsertarYProhibeCambiarDespues()
    {
        var sql = SinComentarios(LoadDdl());

        // Compone la base, no la aplicación: BEFORE INSERT para que lo reciban EF, seeds, migrador
        // e INSERT directo por igual.
        sql.Should().Contain("BEFORE INSERT ON tramites.procedure_instances");
        sql.Should().Contain("EXECUTE FUNCTION tramites.fn_procedure_instances_radicado()");

        // Inmutable (AC4): el UPDATE de cualquiera de las dos columnas falla ruidoso.
        sql.Should().Contain("BEFORE UPDATE OF reference_number, consecutivo ON tramites.procedure_instances");
        sql.Should().Contain("RAISE EXCEPTION");

        // Sin DEFAULT: el trigger es el único que asigna, y así respeta un número explícito.
        sql.Should().Contain("ALTER COLUMN reference_number DROP DEFAULT");
        sql.Should().NotContain("SET DEFAULT");
    }

    [Fact]
    public void ElTriggerRespetaElNumeroPeladoDeLosSeeds()
    {
        // Los seeds 16/21/35 traen rangos sintéticos 91/92/93… (corren antes de que exista la
        // secuencia). Sobre una base migrada, el trigger los lee y compone FTn-9100000001 en vez de
        // gastar un número real. Sin esto, cada arranque de dev (DevelopmentAuthSeeder reejecuta el
        // seed 16) consumiría números de la secuencia aunque las filas ya existan.
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("IF NEW.reference_number ~ '^[1-9][0-9]*$' THEN");
        sql.Should().Contain("NEW.consecutivo := NEW.reference_number::bigint;");
        sql.Should().Contain("NEW.consecutivo := nextval('tramites.procedure_instance_reference_seq');");
    }

    [Fact]
    public void LaRenumeracionConservaElNumeroYSoloTocaElFormatoViejo()
    {
        var sql = SinComentarios(LoadDdl());

        // AC5: MISMO número. El backfill lee consecutivo del texto numérico y compone desde ahí.
        sql.Should().Contain("SET consecutivo = reference_number::bigint");
        // Guard por formato: reejecutar sobre una base ya migrada no reasigna nada.
        sql.Split("reference_number ~ '^[1-9][0-9]*$'").Length.Should().BeGreaterThanOrEqualTo(3,
            "el backfill de consecutivo, el de reference_number y el trigger se guardan por el formato viejo");
    }

    [Fact]
    public void ElNumeroEsUnicoYLaSecuenciaEsSuya()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_consecutivo");
        sql.Should().Contain("OWNED BY tramites.procedure_instances.consecutivo");
        // El índice de apoyo al orden por (longitud, texto) de la HU #12153 se retira: el orden va
        // por la columna numérica.
        sql.Should().Contain("DROP INDEX IF EXISTS tramites.ix_procedure_instances_reference_orden");
    }

    // ── Modelo de EF ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(ProcedureInstance.ReferenceNumber))]
    [InlineData(nameof(ProcedureInstance.Consecutivo))]
    public void EfNuncaMandaLasDosColumnas_NiEnInsertNiEnUpdate(string propiedad)
    {
        using var db = NewContext();
        var p = db.Model.FindEntityType(typeof(ProcedureInstance))!.FindProperty(propiedad)!;

        p.ValueGenerated.Should().Be(ValueGenerated.OnAdd, "EF las lee de vuelta tras el INSERT");
        p.GetBeforeSaveBehavior().Should().Be(PropertySaveBehavior.Ignore, "las asigna el trigger");
        p.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Ignore,
            "son inmutables: un cambio en memoria se descarta en vez de tumbar el SaveChanges contra el trigger");
    }

    [Fact]
    public void ElModeloDeclaraLosTriggersYLaUnicidadDelNumero()
    {
        using var db = NewContext();
        var entidad = db.Model.FindEntityType(typeof(ProcedureInstance))!;

        // Sin declararlos, EF emite el UPDATE con RETURNING que los triggers no admiten.
        entidad.GetDeclaredTriggers().Select(t => t.GetDatabaseName()).Should()
            .Contain(["tr_procedure_instances_radicado", "tr_procedure_instances_radicado_inmutable"]);

        var unico = entidad.GetIndexes().Single(i => i.GetDatabaseName() == "uq_procedure_instances_consecutivo");
        unico.IsUnique.Should().BeTrue();
        unico.Properties.Select(p => p.Name).Should().ContainSingle().Which.Should().Be(nameof(ProcedureInstance.Consecutivo));
    }

    [Fact]
    public void ElOrdenPorRadicadoVaPorElNumeroYSeTraduceASql()
    {
        // La prueba de comportamiento corre sobre EF InMemory; esta comprueba lo que aquella no
        // puede: que Npgsql ordena por la columna numérica en el servidor y no por el texto.
        using var db = NewContext();

        var sql = db.ProcedureInstances.OrderBy(x => x.Consecutivo).ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("consecutivo");
        sql.Should().NotContain("length(", "el orden por (longitud, texto) era el de la HU #12153");
    }
}
