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
