using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12323 (Feature #12254, Épica #12235, ADR-0057) — desactivación sin despliegue (interruptores
/// <c>identity.hierarchy_switches</c>) y auditoría append-only del vínculo padre-hija
/// (<c>identity.tenant_hierarchy_audit</c> + trigger <c>tr_tenants_hierarchy_audit</c>).
/// <para>
/// <b>Qué cubren estas pruebas y qué no.</b> Igual que <see cref="TenantParentHierarchySchemaTests"/>:
/// la suite no tiene Postgres, así que aquí se verifica que el DDL <i>diga</i> lo que debe (seed
/// idempotente, CHECK de claves, trigger LINK/UNLINK con COALESCE del actor, inmutabilidad), que el
/// <c>Down</c> lo revierta en orden inverso, que ninguna sentencia toque <c>is_group_parent</c> (AC2)
/// y que el modelo de EF y el snapshot estén declarados como el DDL. Que el trigger escriba de verdad
/// las filas se prueba contra Postgres real en la validación de la HU.
/// </para>
/// </summary>
public sealed class HierarchySwitchesAndLinkAuditSchemaTests
{
    private const string DdlFile = "108-HU12323-hierarchy-switches-and-link-audit.sql";

    private static readonly string[] ColumnasQueNoVanEnTenants = ["group_read_scope", "inherited_configuration", "is_enabled"];

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new HU12323_HierarchySwitchesAndLinkAudit().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new HU12323_HierarchySwitchesAndLinkAudit().DownOperations.OfType<SqlOperation>().Single().Sql;

    /// <summary>Modelo sin conexión: construir el modelo de EF no abre la base.</summary>
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static IEntityType SwitchEntity(FlitDbContext db) => db.Model.FindEntityType(typeof(HierarchySwitch))!;

    /// <summary>Modelo de diseño: el de runtime no conserva IsDescending ni lo que necesita el differ.</summary>
    private static IModel DesignModel(FlitDbContext db) => db.GetService<IDesignTimeModel>().Model;

    private static IEntityType AuditEntity(FlitDbContext db) => DesignModel(db).FindEntityType(typeof(TenantHierarchyAuditEntry))!;

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    /// <summary>Deja solo las sentencias: quita comentarios <c>--</c> y literales <c>'...'</c> (COMMENT ON, RAISE).</summary>
    private static string StatementsOnly(string sql)
    {
        var sinComentarios = Regex.Replace(sql, @"--[^
]*", string.Empty);
        return Regex.Replace(sinComentarios, @"'[^']*'", "''");
    }

    // ── AC1 · interruptores conmutables sin despliegue ─────────────────────────────────

    [Fact]
    public void AC1_ElDdlCreaLaTablaDeInterruptoresConClavesFijasYDefaultEncendido()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS identity.hierarchy_switches");
        ddl.Should().Contain("switch_key text NOT NULL");
        ddl.Should().Contain("is_enabled boolean NOT NULL DEFAULT true");
        ddl.Should().Contain("CONSTRAINT uq_hierarchy_switches_switch_key UNIQUE (switch_key)");
        ddl.Should().Contain(
            "CONSTRAINT ck_hierarchy_switches_switch_key CHECK (switch_key IN ('group_read_scope', 'inherited_configuration'))");
    }

    [Fact]
    public void AC1_ElSeedDeLosDosInterruptoresEsIdempotenteYNoPisaUnInterruptorApagado()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "INSERT INTO identity.hierarchy_switches (switch_key, is_enabled) VALUES ('group_read_scope', true), ('inherited_configuration', true) ON CONFLICT (switch_key) DO NOTHING");
        Regex.Count(ddl, @"\bINSERT INTO identity\.hierarchy_switches\b").Should().Be(1);
        Regex.IsMatch(ddl, @"\bUPDATE identity\.hierarchy_switches\b").Should().BeFalse("reaplicar no cambia un interruptor apagado a mano");
    }

    [Fact]
    public void AC1_LasClavesDelModeloCoincidenConElCheckDelDdl()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain($"'{HierarchySwitch.GroupReadScopeKey}'");
        ddl.Should().Contain($"'{HierarchySwitch.InheritedConfigurationKey}'");
        HierarchySwitch.GroupReadScopeKey.Should().Be("group_read_scope");
        HierarchySwitch.InheritedConfigurationKey.Should().Be("inherited_configuration");
    }

    [Fact]
    public void AC1_LosInterruptoresLlevanRowVersionYAuditLog()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("row_version bigint NOT NULL DEFAULT 0");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_hierarchy_switches_row_version BEFORE UPDATE ON identity.hierarchy_switches FOR EACH ROW EXECUTE FUNCTION public.trg_row_version()");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_hierarchy_switches_audit AFTER INSERT OR UPDATE OR DELETE ON identity.hierarchy_switches FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log()");
    }

    [Fact]
    public void AC1_ElModeloDeclaraLosInterruptoresComoElDdl()
    {
        using var db = NewContext();
        var entidad = SwitchEntity(db);

        entidad.GetSchema().Should().Be("identity");
        entidad.GetTableName().Should().Be("hierarchy_switches");

        var key = entidad.FindProperty(nameof(HierarchySwitch.SwitchKey))!;
        key.GetColumnName().Should().Be("switch_key");
        key.GetColumnType().Should().Be("text");
        key.IsNullable.Should().BeFalse();

        var enabled = entidad.FindProperty(nameof(HierarchySwitch.IsEnabled))!;
        enabled.GetColumnName().Should().Be("is_enabled");
        enabled.GetDefaultValue().Should().Be(true);

        entidad.FindProperty(nameof(HierarchySwitch.UpdatedBy))!.IsNullable.Should().BeTrue();
        entidad.FindProperty(nameof(HierarchySwitch.UpdatedAt))!.IsNullable.Should().BeFalse();
        entidad.FindProperty(nameof(HierarchySwitch.RowVersion))!.IsConcurrencyToken.Should().BeTrue();

        entidad.GetIndexes().Single().GetDatabaseName().Should().Be("uq_hierarchy_switches_switch_key");
        entidad.GetIndexes().Single().IsUnique.Should().BeTrue();
        entidad.FindProperty("TenantId").Should().BeNull("interruptor global de plataforma, sin tenant_id");
    }

    [Fact]
    public void AC1_UnInterruptorNuevoNaceEncendido()
    {
        new HierarchySwitch().IsEnabled.Should().BeTrue();
    }

    // ── AC2 · los interruptores NO son is_group_parent ─────────────────────────────────

    [Fact]
    public void AC2_NingunaSentenciaDelUpNiDelDownTocaIsGroupParentNiParentTenantId()
    {
        // Solo sentencias: los COMMENT ON explican que los interruptores no reutilizan
        // is_group_parent, y eso no es tocar la columna. El trigger LEE parent_tenant_id de
        // NEW/OLD (auditoría) pero ninguna sentencia la escribe ni la altera.
        var up = StatementsOnly(MigrationUpSql());
        var down = StatementsOnly(MigrationDownSql());

        up.Should().NotContain("is_group_parent");
        down.Should().NotContain("is_group_parent");
        Regex.IsMatch(up, @"\bALTER TABLE identity\.tenants\b").Should().BeFalse("no se altera ninguna columna de tenants");
        Regex.IsMatch(up, @"\bUPDATE identity\.tenants\b").Should().BeFalse();
        Regex.IsMatch(up, @"\bINSERT INTO identity\.tenants\b").Should().BeFalse();
        Regex.IsMatch(up, @"\bDELETE FROM identity\.tenants\b").Should().BeFalse();
        Regex.IsMatch(down, @"\bALTER TABLE identity\.tenants\b").Should().BeFalse();
    }

    [Fact]
    public void AC2_LosInterruptoresVivenEnSuPropiaTablaYNoEnColumnasDeTenants()
    {
        using var db = NewContext();

        SwitchEntity(db).GetTableName().Should().NotBe("tenants");
        db.Model.FindEntityType(typeof(Tenant))!.GetProperties()
            .Select(p => p.GetColumnName())
            .Should().NotContain(ColumnasQueNoVanEnTenants);
    }

    // ── AC3 · auditoría del vínculo: append-only, sobrevive a desvínculos y borrados ───

    [Fact]
    public void AC3_ElDdlCreaLaBitacoraSinFkATenantsYConCheckDeAccion()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS identity.tenant_hierarchy_audit");
        ddl.Should().Contain("parent_tenant_id uuid NOT NULL");
        ddl.Should().Contain("child_tenant_id uuid NOT NULL");
        ddl.Should().Contain("actor_user_id uuid NULL");
        ddl.Should().Contain("occurred_at timestamptz NOT NULL DEFAULT now()");
        ddl.Should().Contain("CONSTRAINT ck_tenant_hierarchy_audit_action CHECK (action IN ('LINK', 'UNLINK'))");

        // Excepción documentada al checklist A7/A8/A9: la fila debe sobrevivir al borrado del padre o del hijo.
        var tabla = ddl[ddl.IndexOf("CREATE TABLE IF NOT EXISTS identity.tenant_hierarchy_audit", StringComparison.Ordinal)..];
        tabla = tabla[..tabla.IndexOf(");", StringComparison.Ordinal)];
        tabla.Should().NotContain("REFERENCES");
        tabla.Should().NotContain("FOREIGN KEY");
        StatementsOnly(LoadUp()).Should().NotContain("fk_tenant_hierarchy_audit");
    }

    [Fact]
    public void AC3_ElDdlCreaLosIndicesPorPadreYPorHijoMasRecientePrimero()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CREATE INDEX IF NOT EXISTS ix_tenant_hierarchy_audit_parent_occurred_at ON identity.tenant_hierarchy_audit (parent_tenant_id, occurred_at DESC)");
        ddl.Should().Contain(
            "CREATE INDEX IF NOT EXISTS ix_tenant_hierarchy_audit_child_occurred_at ON identity.tenant_hierarchy_audit (child_tenant_id, occurred_at DESC)");
    }

    [Fact]
    public void AC3_LaBitacoraEsAppendOnlyForzadaPorTrigger()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_audit_immutable() RETURNS trigger");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenant_hierarchy_audit_immutable BEFORE UPDATE OR DELETE ON identity.tenant_hierarchy_audit FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_audit_immutable()");
        ddl.Should().Contain("es append-only: no se permite %");
        ddl.Should().NotContain("deleted_at", "bitácora append-only: sin soft delete");
    }

    [Fact]
    public void AC3_ElTriggerSobreTenantsEscribeUnlinkDelPadreAnteriorYLinkDelNuevo()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_audit() RETURNS trigger");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenants_hierarchy_audit AFTER INSERT OR UPDATE OF parent_tenant_id ON identity.tenants FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_audit()");

        // En INSERT no hay OLD: el padre anterior es NULL.
        ddl.Should().Contain("v_old_parent := CASE WHEN TG_OP = 'UPDATE' THEN OLD.parent_tenant_id ELSE NULL END");
        ddl.Should().Contain("IF NEW.parent_tenant_id IS NOT DISTINCT FROM v_old_parent THEN RETURN NULL");

        // UNLINK del padre que se deja, LINK del padre nuevo; siempre child = NEW.id.
        ddl.Should().Contain("IF v_old_parent IS NOT NULL THEN INSERT INTO identity.tenant_hierarchy_audit (parent_tenant_id, child_tenant_id, action, actor_user_id) VALUES (v_old_parent, NEW.id, 'UNLINK', v_actor)");
        ddl.Should().Contain("IF NEW.parent_tenant_id IS NOT NULL THEN INSERT INTO identity.tenant_hierarchy_audit (parent_tenant_id, child_tenant_id, action, actor_user_id) VALUES (NEW.parent_tenant_id, NEW.id, 'LINK', v_actor)");
        ddl.IndexOf("'UNLINK', v_actor", StringComparison.Ordinal)
            .Should().BeLessThan(ddl.IndexOf("'LINK', v_actor", StringComparison.Ordinal), "un cambio de padre produce UNLINK y después LINK");
    }

    [Fact]
    public void AC3_ElActorSaleDeLaSesionYCaeAlAutorDeLaFila()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "v_actor := COALESCE( NULLIF(current_setting('app.current_user_id', true), '')::uuid, NEW.updated_by, NEW.created_by)");
    }

    [Fact]
    public void AC3_ElModeloDeclaraLaBitacoraComoElDdlYSinFks()
    {
        using var db = NewContext();
        var entidad = AuditEntity(db);

        entidad.GetSchema().Should().Be("identity");
        entidad.GetTableName().Should().Be("tenant_hierarchy_audit");
        entidad.GetForeignKeys().Should().BeEmpty("la fila sobrevive al borrado del padre o del hijo (excepción documentada A7/A8/A9)");
        entidad.FindProperty("TenantId").Should().BeNull("la fila pertenece a dos tenants; sin tenant_id ni RLS");
        entidad.FindProperty("DeletedAt").Should().BeNull("append-only");
        entidad.FindProperty("RowVersion").Should().BeNull("append-only: nunca se actualiza");

        entidad.FindProperty(nameof(TenantHierarchyAuditEntry.ParentTenantId))!.IsNullable.Should().BeFalse();
        entidad.FindProperty(nameof(TenantHierarchyAuditEntry.ChildTenantId))!.IsNullable.Should().BeFalse();
        entidad.FindProperty(nameof(TenantHierarchyAuditEntry.ActorUserId))!.IsNullable.Should().BeTrue();
        entidad.FindProperty(nameof(TenantHierarchyAuditEntry.Action))!.GetColumnType().Should().Be("text");
        entidad.FindProperty(nameof(TenantHierarchyAuditEntry.OccurredAt))!.GetDefaultValueSql().Should().Be("now()");

        var indices = entidad.GetIndexes().ToDictionary(i => i.GetDatabaseName()!);
        indices.Should().ContainKeys("ix_tenant_hierarchy_audit_parent_occurred_at", "ix_tenant_hierarchy_audit_child_occurred_at");
        indices["ix_tenant_hierarchy_audit_parent_occurred_at"].Properties.Select(p => p.Name)
            .Should().Equal(nameof(TenantHierarchyAuditEntry.ParentTenantId), nameof(TenantHierarchyAuditEntry.OccurredAt));
        indices["ix_tenant_hierarchy_audit_parent_occurred_at"].IsDescending.Should().Equal(false, true);
        indices["ix_tenant_hierarchy_audit_child_occurred_at"].Properties.Select(p => p.Name)
            .Should().Equal(nameof(TenantHierarchyAuditEntry.ChildTenantId), nameof(TenantHierarchyAuditEntry.OccurredAt));
        indices["ix_tenant_hierarchy_audit_child_occurred_at"].IsDescending.Should().Equal(false, true);
    }

    [Fact]
    public void AC3_LasAccionesDelModeloCoincidenConElCheckDelDdl()
    {
        TenantHierarchyAuditEntry.LinkAction.Should().Be("LINK");
        TenantHierarchyAuditEntry.UnlinkAction.Should().Be("UNLINK");
    }

    // ── Migración: Up = DDL embebido, idempotente, Down inverso ────────────────────────

    [Fact]
    public void ElUpDeLaMigracionEsElDdlEmbebido()
    {
        MigrationUpSql().Should().Be(LoadUp());
    }

    [Fact]
    public void ElUpEsIdempotente()
    {
        var ddl = Normalize(LoadUp());

        Regex.Count(ddl, "CREATE TABLE IF NOT EXISTS").Should().Be(2);
        Regex.Count(ddl, "CREATE INDEX IF NOT EXISTS").Should().Be(2);
        Regex.Count(ddl, "CREATE OR REPLACE FUNCTION").Should().Be(2);
        Regex.Count(ddl, "DROP TRIGGER IF EXISTS").Should().Be(4);
        Regex.Count(ddl, "CREATE TRIGGER").Should().Be(4);
        ddl.Should().Contain("ON CONFLICT (switch_key) DO NOTHING");
        Regex.IsMatch(ddl, @"\bCREATE TABLE (?!IF NOT EXISTS)").Should().BeFalse();
    }

    [Fact]
    public void ElDownRevierteTodoEnOrdenInverso()
    {
        var down = Normalize(MigrationDownSql());

        var pasos = new[]
        {
            "DROP TRIGGER IF EXISTS tr_tenants_hierarchy_audit ON identity.tenants",
            "DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_audit()",
            "DROP TRIGGER IF EXISTS tr_tenant_hierarchy_audit_immutable ON identity.tenant_hierarchy_audit",
            "DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_audit_immutable()",
            "DROP INDEX IF EXISTS identity.ix_tenant_hierarchy_audit_child_occurred_at",
            "DROP INDEX IF EXISTS identity.ix_tenant_hierarchy_audit_parent_occurred_at",
            "DROP TABLE IF EXISTS identity.tenant_hierarchy_audit",
            "DROP TRIGGER IF EXISTS tr_hierarchy_switches_audit ON identity.hierarchy_switches",
            "DROP TRIGGER IF EXISTS tr_hierarchy_switches_row_version ON identity.hierarchy_switches",
            "DROP TABLE IF EXISTS identity.hierarchy_switches",
        };

        var posiciones = pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).ToArray();
        posiciones.Should().OnlyContain(i => i >= 0, "el Down debe retirar cada artefacto que creó el Up");
        posiciones.Should().BeInAscendingOrder("trigger de tenants → bitácora → interruptores");

        // El Down no toca nada que no haya creado el Up: ni la jerarquía de #12318 ni tenants.
        down.Should().NotContain("tr_tenants_hierarchy ON");
        down.Should().NotContain("trg_tenant_hierarchy_depth");
        down.Should().NotContain("DROP COLUMN");
    }

    // ── Snapshot: el modelo y el snapshot coinciden para las dos tablas nuevas ─────────

    [Fact]
    public void ElSnapshotNoTieneDriftSobreLasTablasNuevas()
    {
        using var db = NewContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var snapshotFinal = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var diferencias = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotFinal.GetRelationalModel(), DesignModel(db).GetRelationalModel());

        var tablas = new[] { "hierarchy_switches", "tenant_hierarchy_audit" };
        var sobreLasNuevas = diferencias
            .Where(op => tablas.Any(t => Describe(op).Contains(t, StringComparison.Ordinal)))
            .Select(op => $"{op.GetType().Name}: {Describe(op)}")
            .ToList();

        sobreLasNuevas.Should().BeEmpty("el snapshot escrito a mano debe coincidir con las configuraciones EF");
    }

    private static string Describe(MigrationOperation op)
    {
        var tabla = op is ITableMigrationOperation t ? $"{t.Schema}.{t.Table}" : string.Empty;
        var nombre = op.GetType().GetProperty("Name")?.GetValue(op) as string;
        return string.IsNullOrEmpty(nombre) ? tabla : $"{tabla}.{nombre}";
    }
}
