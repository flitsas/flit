using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12151 (Feature #12150) — el radicado pasa a ser un consecutivo GLOBAL asignado por
/// <c>tramites.procedure_instance_reference_seq</c>.
/// <para>
/// <b>Qué cubren estas pruebas y qué no.</b> El esquema lo lleva SQL crudo (la tabla está
/// <c>ExcludeFromMigrations</c>) y <b>la suite no tiene Postgres</b> —el repo no usa
/// Testcontainers, ver <see cref="NotificationDeliveryLogSchemaTests"/>—. Así que aquí se verifica
/// que el DDL <i>diga</i> lo que debe y que el modelo de EF <i>esté declarado</i> como debe. Que el
/// motor lo ejecute correctamente (AC1 a AC4 de punta a punta) se comprobó a mano contra una copia
/// de la base de dev; queda en las evidencias de la HU.
/// </para>
/// <para>
/// La prueba del modelo no es decorativa: las tres piezas que exige
/// <c>ProcedureInstanceConfiguration</c> son justo las que, si faltan, hacen que EF mande cadena
/// vacía y el <c>DEFAULT</c> nunca se dispare.
/// </para>
/// </summary>
public sealed class ConsecutivoGlobalTramiteTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.103-HU12151-consecutivo-global-tramite.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Modelo sin conexión: construir el modelo de EF no abre la base.</summary>
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static IProperty ReferenceNumberProperty(FlitDbContext db) =>
        db.Model.FindEntityType(typeof(ProcedureInstance))!
            .FindProperty(nameof(ProcedureInstance.ReferenceNumber))!;

    // ── DDL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ElDdlCreaLaSecuenciaYLaPoneComoDefaultDeLaColumna()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CREATE SEQUENCE IF NOT EXISTS tramites.procedure_instance_reference_seq");
        ddl.Should().Contain("SET DEFAULT nextval('tramites.procedure_instance_reference_seq')::text");
    }

    [Fact]
    public void ElDdlSustituyeLaUnicidadPorTenantPorUnaGlobal()
    {
        var ddl = LoadDdl();

        // Es una CONSTRAINT, no un índice suelto: con DROP INDEX el script falla.
        ddl.Should().Contain("DROP CONSTRAINT IF EXISTS uq_procedure_instances_tenant_reference");
        ddl.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_reference");

        // La llave global NO puede llevar tenant_id: eso era exactamente el defecto.
        var indice = ddl[ddl.IndexOf("CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instances_reference", StringComparison.Ordinal)..];
        indice[..indice.IndexOf(';', StringComparison.Ordinal)].Should().NotContain("tenant_id");
    }

    [Fact]
    public void ElDdlObligaAlFormatoNumerico()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("ck_procedure_instances_reference_numerico");
        ddl.Should().Contain("CHECK (reference_number ~ '^[0-9]+$')");
    }

    [Fact]
    public void LaRenumeracionEsPorAntiguedadYSoloCorreUnaVez()
    {
        var ddl = LoadDdl();

        // El más antiguo es el 1 (AC1).
        ddl.Should().Contain("row_number() OVER (ORDER BY created_at, id)");

        // El guard: sin él, reejecutar el script reasignaría radicados ya emitidos y rompería la
        // inmutabilidad del AC5.
        ddl.Should().Contain("IF NOT EXISTS (")
           .And.Contain("uq_procedure_instances_reference");
        ddl.Should().Contain("setval(");
    }

    [Fact]
    public void ElDdlNoCreaElIndiceDeExpresionNumerico()
    {
        // No puede ir en la misma transacción que la renumeración: el UPDATE es no-HOT (la columna
        // está indexada), así que CREATE INDEX evalúa el cast también sobre las versiones viejas de
        // fila y revienta con «invalid input syntax for type bigint». Va en la HU #12153.
        LoadDdl().Should().NotContain("::bigint))");
    }

    // ── Seeds (HU #12152) ────────────────────────────────────────────────────

    /// <summary>Los seeds que insertan trámites y por tanto tocan el radicado.</summary>
    public static TheoryData<string> SeedsDeTramites() =>
    [
        "Flit.Infrastructure.Persistence.Sql.Ddl.16-HU10133-ot-admin-dev-seed.sql",
        "Flit.Infrastructure.Persistence.Sql.Ddl.21-HU10240-analytics-dev-seed.sql",
        "Flit.Infrastructure.Persistence.Sql.Ddl.35-F11-log-qx-mock-seed.sql",
    ];

    /// <summary>Quita los comentarios de línea SQL para no medir lo que dice la documentación.</summary>
    private static string SinComentarios(string sql) =>
        string.Join('\n', sql.Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));

    private static string LoadResource(string recurso)
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(recurso);
        stream.Should().NotBeNull($"el DDL embebido {recurso} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Theory]
    [MemberData(nameof(SeedsDeTramites))]
    public void NingunSeedSeApoyaEnLaConstraintPorTenant(string recurso)
    {
        // uq_procedure_instances_tenant_reference deja de existir tras la migración 103, así que
        // un ON CONFLICT que la nombre falla con «there is no unique or exclusion constraint
        // matching the ON CONFLICT specification» al reejecutar el seed sobre una base migrada.
        // La llave natural de estos seeds es el id, que ya era fijo.
        SinComentarios(LoadResource(recurso)).Should()
            .NotContain("ON CONFLICT (tenant_id, reference_number)");
    }

    [Theory]
    [MemberData(nameof(SeedsDeTramites))]
    public void LosRadicadosQueEscribenLosSeedsSonNumericos(string recurso)
    {
        // Estos seeds corren ANTES de la migración 103, cuando reference_number es NOT NULL y aún
        // no tiene DEFAULT: tienen que traer un valor. Y ese valor debe ser numérico, o el CHECK
        // que añade la 103 rechazaría la fila al renumerar. De ahí los rangos sintéticos 91/92/93.
        // Solo el SQL: los comentarios de estos mismos archivos citan los prefijos viejos para
        // explicar por qué se fueron, y eso no debe hacer fallar la prueba.
        var sql = SinComentarios(LoadResource(recurso));

        foreach (var prefijoViejo in new[] { "OT-DEV-", "SEED-ANL-", "QXSEED-" })
            sql.Should().NotContain($"'{prefijoViejo}", $"{prefijoViejo} no es numérico y violaría el CHECK");
    }

    // ── Orden numérico (HU #12153) ───────────────────────────────────────────

    [Fact]
    public void ElOrdenPorRadicadoSeTraduceASqlYNoSeEvaluaEnCliente()
    {
        // La prueba de comportamiento del orden corre sobre EF InMemory, que traduce cosas que
        // Postgres no. Esta comprueba lo que aquella no puede: que Npgsql genera el ORDER BY en
        // el servidor. Si length() no fuera traducible, EF Core lanzaría en ejecución — y el
        // listado entero se caería, no solo el orden.
        using var db = NewContext();

        var sql = db.ProcedureInstances
            .OrderBy(x => x.ReferenceNumber.Length)
            .ThenBy(x => x.ReferenceNumber)
            .ToQueryString();

        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("length(", "el orden por longitud debe resolverse en Postgres");
        sql.Should().NotContain("::bigint", "se evita el cast a propósito: puede fallar en ejecución");
    }

    [Fact]
    public void ElDdl104ExigeEnteroSinCerosALaIzquierda()
    {
        // La equivalencia entre ORDER BY (longitud, texto) y el orden numérico solo se sostiene si
        // no hay ceros a la izquierda: '0123' se ordenaría después de '999'. El CHECK lo impone.
        // Sin comentarios: el propio script explica por qué NO se usa ::bigint, y esa mención
        // no debe hacer fallar la comprobación.
        var sql = SinComentarios(LoadResource(
            "Flit.Infrastructure.Persistence.Sql.Ddl.104-HU12153-orden-numerico-radicado.sql"));

        sql.Should().Contain("CHECK (reference_number ~ '^[1-9][0-9]*$')");
        sql.Should().Contain("ix_procedure_instances_reference_orden");
        sql.Should().Contain("length(reference_number), reference_number");
        sql.Should().NotContain("::bigint", "el índice de apoyo no lleva cast");
    }

    // ── Modelo de EF ─────────────────────────────────────────────────────────

    [Fact]
    public void EfNoMandaElRadicadoEnElInsertYLoLeeDeVuelta()
    {
        using var db = NewContext();
        var propiedad = ReferenceNumberProperty(db);

        // Sin esto EF enviaría el string.Empty con el que se construye la entidad, el DEFAULT no se
        // dispararía y el segundo trámite chocaría contra el índice único.
        propiedad.GetBeforeSaveBehavior().Should().Be(PropertySaveBehavior.Ignore);

        // Y sin esto EF no traería de vuelta el valor que asignó la base.
        propiedad.ValueGenerated.Should().Be(ValueGenerated.OnAdd);

        propiedad.GetDefaultValueSql()
            .Should().NotBeNull()
            .And.Subject.As<string>()
            .Should().Contain("procedure_instance_reference_seq");
    }

    [Fact]
    public void ElModeloDeclaraLaUnicidadGlobalYNoLaDeTenant()
    {
        using var db = NewContext();
        var entidad = db.Model.FindEntityType(typeof(ProcedureInstance))!;

        var global = entidad.GetIndexes()
            .Single(i => i.GetDatabaseName() == "uq_procedure_instances_reference");

        global.IsUnique.Should().BeTrue();
        global.Properties.Select(p => p.Name).Should()
            .ContainSingle().Which.Should().Be(nameof(ProcedureInstance.ReferenceNumber));

        entidad.GetIndexes().Select(i => i.GetDatabaseName())
            .Should().NotContain("uq_procedure_instances_tenant_reference");
    }
}
