using System.Text.RegularExpressions;
using Flit.Admin.Domain.Companies.Create;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12406 (Feature #12254, Épica #12235) — clase de la cabeza de grupo como tipo de compañía
/// (CONCESION | MARCA_BLANCA) y padre de la compañía radicadora conservado en el trámite. Misma
/// división que <see cref="TenantParentHierarchySchemaTests"/>: aquí se verifica que el DDL 109
/// <i>diga</i> lo que debe (catálogo ampliado, CHECK de acoplamiento, rama (d) del trigger, columna del
/// trámite sin FK, trigger de inmutabilidad), que el <c>Down</c> lo revierta completo y en orden, que
/// no haya poblado, y que el modelo de EF y el catálogo del dominio estén como deben (sin conexión).
/// Que el motor rechace de verdad se prueba contra PostgreSQL real en
/// <c>Flit.Integration.Tests/Hierarchy</c>.
/// <para>
/// Uso de ejemplo: <c>EmbeddedDdl.LoadUp("109-HU12406-head-tenant-types-and-parent-snapshot.sql")</c> y
/// <c>new HU12406_HeadTenantTypesAndParentSnapshot().DownOperations</c>.
/// </para>
/// </summary>
public sealed class HeadTenantTypeAndParentSnapshotSchemaTests
{
    private const string DdlFile = "109-HU12406-head-tenant-types-and-parent-snapshot.sql";

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new HU12406_HeadTenantTypesAndParentSnapshot().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new HU12406_HeadTenantTypesAndParentSnapshot().DownOperations.OfType<SqlOperation>().Single().Sql;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    private static string StatementsOnly(string sql)
    {
        var sinComentarios = Regex.Replace(sql, @"--[^
]*", string.Empty);
        return Regex.Replace(sinComentarios, @"'[^']*'", "''");
    }

    // ── AC1 · clase obligatoria en la cabeza: catálogo ampliado + acoplamiento ─────────

    [Fact]
    public void AC1_ElDdlAmpliaElCatalogoDeTiposConLosDosTiposDeCabeza()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("ALTER TABLE identity.tenants DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type;");
        ddl.Should().Contain(
            "ADD CONSTRAINT ck_tenants_tenant_type CHECK (tenant_type IN ('RENTING', 'CONCESIONARIO', 'FLIT', 'CONCESION', 'MARCA_BLANCA'))");
    }

    [Fact]
    public void AC1_ElDdlAcoplaIsGroupParentAlTipoSinCorreccionSilenciosa()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "ADD CONSTRAINT ck_tenants_group_parent_by_type CHECK (is_group_parent = (tenant_type IN ('CONCESION', 'MARCA_BLANCA')))");
        StatementsOnly(LoadUp()).Should().NotContain("NEW.is_group_parent :=", "fail-closed: nadie corrige is_group_parent en silencio");
    }

    [Fact]
    public void AC1_ElCatalogoDelDominioYElMapeoDeCabezasCoincidenConElCheck()
    {
        CompanyTenantTypes.All.Should().BeEquivalentTo(["RENTING", "CONCESIONARIO", "FLIT", "CONCESION", "MARCA_BLANCA"]);
        HeadTenantTypes.All.Should().BeEquivalentTo(["CONCESION", "MARCA_BLANCA"]);

        HeadTenantTypes.IsHead("CONCESION").Should().BeTrue();
        HeadTenantTypes.IsHead("MARCA_BLANCA").Should().BeTrue();
        HeadTenantTypes.IsHead("CONCESIONARIO").Should().BeFalse();
        HeadTenantTypes.IsHead("RENTING").Should().BeFalse();
        HeadTenantTypes.IsHead("FLIT").Should().BeFalse();
        HeadTenantTypes.IsHead("concesion").Should().BeFalse("sin normalizar: lo que hay en la base es lo que vale");
        HeadTenantTypes.IsHead(null).Should().BeFalse();

        // Un único mapeo: el dominio de consultas usa los mismos literales.
        GroupKindCodes.ToCode(GroupKind.Concesion).Should().Be(HeadTenantTypes.Concesion);
        GroupKindCodes.ToCode(GroupKind.MarcaBlanca).Should().Be(HeadTenantTypes.MarcaBlanca);
        GroupKindCodes.TryParse("CONCESION", out var c).Should().BeTrue();
        c.Should().Be(GroupKind.Concesion);
        GroupKindCodes.TryParse("MARCA_BLANCA", out var m).Should().BeTrue();
        m.Should().Be(GroupKind.MarcaBlanca);
        GroupKindCodes.TryParse("CONCESIONARIO", out _).Should().BeFalse();
        GroupKindCodes.TryParse(null, out _).Should().BeFalse();

        new NewCompany("X", "1", "X", "CONCESION", true, null).IsGroupParent.Should().BeTrue();
        new NewCompany("X", "1", "X", "MARCA_BLANCA", true, null).IsGroupParent.Should().BeTrue();
        new NewCompany("X", "1", "X", "CONCESIONARIO", true, null).IsGroupParent.Should().BeFalse();
    }

    [Fact]
    public void AC1_ElModeloNoDeclaraNingunaColumnaDeClaseAparte()
    {
        using var db = NewContext();
        var entidad = db.Model.FindEntityType(typeof(Tenant))!;

        // El contexto sin conexión no aplica UseSnakeCaseNamingConvention: se afirma sobre nombres de
        // propiedad y sobre las columnas configuradas explícitamente.
        entidad.GetProperties().Select(p => p.Name).Should().NotContain("GroupKind");
        entidad.GetProperties().Select(p => p.GetColumnName()).Should().NotContain("group_kind");
        entidad.FindProperty(nameof(Tenant.TenantType)).Should().NotBeNull();
        entidad.FindProperty(nameof(Tenant.TenantType))!.GetMaxLength().Should().Be(20, "CONCESION y MARCA_BLANCA caben en varchar(20)");
        entidad.FindProperty(nameof(Tenant.IsGroupParent))!.GetColumnName().Should().Be("is_group_parent");
    }

    // ── AC3 · clase inmutable con hijos vigentes (rama (d)) ────────────────────────────

    [Fact]
    public void AC3_ElTriggerRechazaCambiarElTipoDeUnaCabezaConHijos()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "IF NEW.tenant_type IS DISTINCT FROM OLD.tenant_type AND (OLD.tenant_type IN ('CONCESION', 'MARCA_BLANCA') OR NEW.tenant_type IN ('CONCESION', 'MARCA_BLANCA')) AND v_has_children THEN");
        ddl.Should().Contain("la clase de la cabeza de grupo (tenant_type) no puede cambiar");
        ddl.Should().Contain("v_has_children := EXISTS (SELECT 1 FROM identity.tenants c WHERE c.parent_tenant_id = NEW.id)");
    }

    [Fact]
    public void AC3_ElTriggerSeDisparaTambienEnUpdateOfTenantTypeYConservaLasRamasAnteriores()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_depth() RETURNS trigger");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenants_hierarchy BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent, tenant_type ON identity.tenants FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_depth()");

        // (a)(b)(c) de 107-HU12318 siguen ahí, con los mismos textos que afirma la suite de integración.
        ddl.Should().Contain("una cabeza de grupo (is_group_parent) no puede tener padre (parent_tenant_id)");
        ddl.Should().Contain("no es cabeza de grupo (is_group_parent = false)");
        ddl.Should().Contain("ya tiene padre; profundidad máxima 2");
        ddl.Should().Contain("tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre");
    }

    [Fact]
    public void AC3_TodosLosRechazosDelDdlUsanErrcodeCheckViolation()
    {
        var ddl = LoadUp();

        var raises = Regex.Count(ddl, @"RAISE EXCEPTION");
        var errcodes = Regex.Count(ddl, @"USING ERRCODE = 'check_violation'");

        raises.Should().Be(7, "(a)x3, (b), (c), (d) tipo con hijos, y el snapshot inmutable del trámite");
        errcodes.Should().Be(raises);
    }

    // ── AC4 · la clase acompaña a la marca de cabeza en el alcance ─────────────────────

    [Fact]
    public void AC4_ElAlcanceDeGrupoExponeLaClase()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();

        var concesion = TenantScope.Group(parent, [child], GroupKind.Concesion);
        var marca = TenantScope.Group(parent, [child], GroupKind.MarcaBlanca);

        concesion.IsGroup.Should().BeTrue();
        concesion.GroupKind.Should().Be(GroupKind.Concesion);
        marca.GroupKind.Should().Be(GroupKind.MarcaBlanca);
        concesion.ReadTenantIds.Should().BeEquivalentTo([parent, child]);

        TenantScope.Single(parent).GroupKind.Should().BeNull("un cliente sin jerarquía no tiene clase");
        TenantScope.Group(parent, [], GroupKind.Concesion).GroupKind.Should().BeNull("sin hijos degrada a Single");

        var invalida = () => TenantScope.Group(parent, [child], (GroupKind)99);
        invalida.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── AC5 · el trámite conserva el padre; ninguna ruta posterior lo modifica ─────────

    [Fact]
    public void AC5_ElDdlAgregaParentTenantIdAtCreationSinFkYConTriggerDeInmutabilidad()
    {
        var ddl = Normalize(LoadUp());
        var sentencias = Normalize(StatementsOnly(LoadUp()));

        ddl.Should().Contain("ALTER TABLE tramites.procedure_instances ADD COLUMN IF NOT EXISTS parent_tenant_id_at_creation uuid NULL");
        sentencias.Should().NotContain("REFERENCES identity.tenants", "trazabilidad, no integridad referencial: sin FK a propósito");
        sentencias.Should().NotContain("FOREIGN KEY");

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION tramites.trg_procedure_instance_parent_snapshot_immutable() RETURNS trigger");
        ddl.Should().Contain("IF NEW.parent_tenant_id_at_creation IS DISTINCT FROM OLD.parent_tenant_id_at_creation THEN");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_procedure_instances_parent_snapshot_immutable BEFORE UPDATE OF parent_tenant_id_at_creation ON tramites.procedure_instances FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_instance_parent_snapshot_immutable()");
    }

    [Fact]
    public void AC5_ElModeloMapeaElSnapshotComoGuidNullableDeSoloInsercion()
    {
        using var db = NewContext();
        var entidad = db.Model.FindEntityType(typeof(ProcedureInstance))!;
        var propiedad = entidad.FindProperty(nameof(ProcedureInstance.ParentTenantIdAtCreation))!;

        propiedad.ClrType.Should().Be<Guid?>();
        propiedad.IsNullable.Should().BeTrue();
        propiedad.GetColumnName().Should().Be("parent_tenant_id_at_creation");
        propiedad.GetBeforeSaveBehavior().Should().Be(PropertySaveBehavior.Save, "viaja en el INSERT del trámite");
        propiedad.GetAfterSaveBehavior().Should().Be(PropertySaveBehavior.Throw, "ninguna ruta de escritura posterior puede modificarlo");

        entidad.GetForeignKeys()
            .Should().NotContain(fk => fk.Properties.Any(p => p.Name == nameof(ProcedureInstance.ParentTenantIdAtCreation)),
                "sin FK: el valor debe sobrevivir al desvínculo y al borrado del padre");
    }

    [Fact]
    public void AC5_UnTramiteNuevoNaceSinPadreConservado()
    {
        new ProcedureInstance().ParentTenantIdAtCreation.Should().BeNull();
    }

    // ── AC7 · paridad: el tipo de quien no es cabeza no cambia de significado ──────────

    [Fact]
    public void AC7_LosTresTiposAnterioresSiguenEnElCatalogoYNoSonDeCabeza()
    {
        foreach (var tipo in new[] { "RENTING", "CONCESIONARIO", "FLIT" })
        {
            CompanyTenantTypes.IsValid(tipo).Should().BeTrue();
            HeadTenantTypes.IsHead(tipo).Should().BeFalse();
        }

        // La migración no reescribe ningún tipo existente ni is_group_parent.
        var sentencias = Normalize(StatementsOnly(LoadUp()));
        Regex.IsMatch(sentencias, @"\bUPDATE identity\.tenants\b").Should().BeFalse();
    }

    // ── AC8 · sin poblado, idempotente, reversible ─────────────────────────────────────

    [Fact]
    public void AC8_ElDdlNoPueblaNingunaFila()
    {
        var ddl = Normalize(StatementsOnly(LoadUp()));

        Regex.IsMatch(ddl, @"\bUPDATE identity\.tenants\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bUPDATE tramites\.procedure_instances\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bINSERT INTO\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bDELETE FROM\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bDEFAULT\b").Should().BeFalse("la columna nueva nace NULL, sin default que pueble");
    }

    [Fact]
    public void AC8_ElUpEsIdempotente()
    {
        var ddl = Normalize(LoadUp());

        Regex.Count(ddl, "ADD COLUMN IF NOT EXISTS").Should().Be(1);
        ddl.Should().Contain("DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type");
        ddl.Should().Contain("IF NOT EXISTS ( SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenants_group_parent_by_type')");
        Regex.Count(ddl, "CREATE OR REPLACE FUNCTION").Should().Be(2);
        ddl.Should().Contain("DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants");
        ddl.Should().Contain("DROP TRIGGER IF EXISTS tr_procedure_instances_parent_snapshot_immutable ON tramites.procedure_instances");
    }

    [Fact]
    public void AC8_ElUpDeLaMigracionEsElDdlEmbebido()
    {
        MigrationUpSql().Should().Be(LoadUp());
    }

    [Fact]
    public void AC8_ElDownRevierteTodoEnOrdenInversoYRestauraElTriggerDe107YElCatalogoDeTres()
    {
        var down = Normalize(MigrationDownSql());

        var pasos = new[]
        {
            "DROP TRIGGER IF EXISTS tr_procedure_instances_parent_snapshot_immutable ON tramites.procedure_instances",
            "DROP FUNCTION IF EXISTS tramites.trg_procedure_instance_parent_snapshot_immutable()",
            "ALTER TABLE tramites.procedure_instances DROP COLUMN IF EXISTS parent_tenant_id_at_creation",
            "CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_depth() RETURNS trigger",
            "CREATE TRIGGER tr_tenants_hierarchy BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent ON identity.tenants",
            "DROP CONSTRAINT IF EXISTS ck_tenants_group_parent_by_type",
            "DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type",
            "ADD CONSTRAINT ck_tenants_tenant_type CHECK (tenant_type IN ('RENTING', 'CONCESIONARIO', 'FLIT'))",
        };

        var posiciones = pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).ToArray();
        posiciones.Should().OnlyContain(i => i >= 0, "el Down debe retirar cada artefacto que creó el Up");
        posiciones.Should().BeInAscendingOrder("trámite → función restaurada → trigger → acoplamiento → catálogo de tres");

        // La función restaurada es la de 107: sin rama (d) sobre tenant_type.
        var funcion = down[down.IndexOf("CREATE OR REPLACE FUNCTION", StringComparison.Ordinal)..down.IndexOf("DROP TRIGGER IF EXISTS tr_tenants_hierarchy", StringComparison.Ordinal)];
        funcion.Should().NotContain("tenant_type");
        funcion.Should().Contain("profundidad máxima 2");
        funcion.Should().Contain("tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre");
    }

    [Fact]
    public void AC8_LaMigracionSigueALaDe12323()
    {
        // GetMigrations enumera las migraciones del ensamblado sin abrir conexión.
        using var db = NewContext();
        var migraciones = db.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToList();

        migraciones.Should().Contain("20260910140000_HU12406_HeadTenantTypesAndParentSnapshot");
        migraciones.Should().Contain("20260910130000_HU12323_HierarchySwitchesAndLinkAudit");
    }

    // ── Sin cambio observable ───────────────────────────────────────────────────────────

    [Fact]
    public void UnTenantNuevoNaceSinSerCabeza()
    {
        var tenant = new Tenant();

        tenant.IsGroupParent.Should().BeFalse();
        HeadTenantTypes.IsHead(tenant.TenantType).Should().BeFalse();
    }
}
