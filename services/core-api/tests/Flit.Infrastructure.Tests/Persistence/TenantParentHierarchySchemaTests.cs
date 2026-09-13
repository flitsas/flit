using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12318 (Feature #12254, Épica #12235) — jerarquía de clientes padre-hija con profundidad 2
/// forzada por la base de datos.
/// <para>
/// <b>Qué cubren estas pruebas y qué no.</b> El invariante vive en SQL (FK RESTRICT, CHECK y
/// trigger PL/pgSQL) y <b>la suite no tiene Postgres</b> —el repo no usa Testcontainers, ver
/// <see cref="ConsecutivoGlobalTramiteTests"/>—. Aquí se verifica que el DDL <i>diga</i> lo que
/// debe, que el <c>Down</c> lo revierta completo, que no haya backfill, y que el modelo de EF
/// <i>esté declarado</i> como debe. Que el motor rechace de verdad el tercer nivel, el padre que no
/// es cabeza de grupo o la autorreferencia (AC1, AC2, AC4, AC5, AC6 en runtime) se prueba contra
/// Postgres real en la HU #12319.
/// </para>
/// </summary>
public sealed class TenantParentHierarchySchemaTests
{
    private const string DdlFile = "107-HU12318-tenant-parent-hierarchy.sql";

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new HU12318_TenantParentHierarchy().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new HU12318_TenantParentHierarchy().DownOperations.OfType<SqlOperation>().Single().Sql;

    /// <summary>Modelo sin conexión: construir el modelo de EF no abre la base.</summary>
    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static IEntityType TenantEntity(FlitDbContext db) =>
        db.Model.FindEntityType(typeof(Tenant))!;

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    /// <summary>Deja solo las sentencias: quita comentarios <c>--</c> y literales <c>'...'</c> (COMMENT ON, RAISE).</summary>
    private static string StatementsOnly(string sql)
    {
        var sinComentarios = Regex.Replace(sql, @"--[^
]*", string.Empty);
        return Regex.Replace(sinComentarios, @"'[^']*'", "''");
    }

    // ── AC3 · un único padre por cliente: columna escalar, sin tabla puente ─────────────

    [Fact]
    public void AC3_ElDdlAgregaParentTenantIdComoColumnaEscalarNullableSinTablaPuente()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("ALTER TABLE identity.tenants ADD COLUMN IF NOT EXISTS parent_tenant_id uuid NULL");
        ddl.Should().NotContain("CREATE TABLE", "un único padre por cliente: columna escalar, no tabla puente");
    }

    [Fact]
    public void AC3_ElModeloDeclaraParentTenantIdComoGuidNullableEnSnakeCase()
    {
        using var db = NewContext();
        var propiedad = TenantEntity(db).FindProperty(nameof(Tenant.ParentTenantId))!;

        propiedad.ClrType.Should().Be<Guid?>();
        propiedad.IsNullable.Should().BeTrue();
        propiedad.GetColumnName().Should().Be("parent_tenant_id");
    }

    // ── AC6 · borrado del padre con hijos: FK ON DELETE RESTRICT ────────────────────────

    [Fact]
    public void AC6_ElDdlDeclaraLaFkAutoReferenteConOnDeleteRestrict()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "ADD CONSTRAINT fk_tenants_parent_tenant FOREIGN KEY (parent_tenant_id) REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE");
    }

    [Fact]
    public void AC6_ElModeloDeclaraLaFkConDeleteBehaviorRestrict()
    {
        using var db = NewContext();
        var entidad = TenantEntity(db);

        var fk = entidad.GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(Tenant.ParentTenantId));

        fk.PrincipalEntityType.Should().BeSameAs(entidad, "la FK es auto-referente");
        fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        fk.GetConstraintName().Should().Be("fk_tenants_parent_tenant");
        fk.IsRequired.Should().BeFalse();
    }

    // ── AC5 · autorreferencia rechazada ────────────────────────────────────────────────

    [Fact]
    public void AC5_ElDdlDeclaraElCheckAntiAutorreferencia()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "ADD CONSTRAINT ck_tenants_parent_not_self CHECK (parent_tenant_id IS NULL OR parent_tenant_id <> id)");
    }

    // ── AC2 · tercer nivel rechazado por la base (trigger, no aplicación) ──────────────

    [Fact]
    public void AC2_ElDdlCreaElTriggerBeforeInsertOrUpdateSobreTenants()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_depth() RETURNS trigger");
        ddl.Should().Contain("DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenants_hierarchy BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent ON identity.tenants FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_depth()");
    }

    [Fact]
    public void AC2_ElTriggerRechazaQueElPadreTengaPadreYQueUnHijoSeaCabezaDeGrupo()
    {
        var ddl = Normalize(LoadUp());

        // (a) el padre no puede tener padre a su vez → profundidad máxima 2.
        ddl.Should().Contain("IF v_parent_parent_id IS NOT NULL THEN");
        ddl.Should().Contain("profundidad máxima 2");

        // (b) un hijo no puede ser cabeza de grupo (ni una cabeza colgar de nadie).
        ddl.Should().Contain("IF NEW.is_group_parent IS TRUE AND NEW.parent_tenant_id IS NOT NULL THEN");

        // (c) quien tiene hijos no puede dejar de ser cabeza ni recibir padre (cierra el rodeo en dos pasos).
        ddl.Should().Contain("EXISTS (SELECT 1 FROM identity.tenants c WHERE c.parent_tenant_id = NEW.id)");
    }

    [Fact]
    public void AC2_TodosLosRechazosDelTriggerUsanErrcodeCheckViolation()
    {
        var ddl = LoadUp();

        var raises = Regex.Count(ddl, @"RAISE EXCEPTION");
        var errcodes = Regex.Count(ddl, @"USING ERRCODE = 'check_violation'");

        raises.Should().BeGreaterThanOrEqualTo(5, "(a) padre inexistente, (a) padre no cabeza, (a) padre con padre, (b) cabeza con padre, (c) con hijos");
        errcodes.Should().Be(raises, "cada rechazo del trigger debe salir como check_violation para que la app lo traduzca igual que un CHECK");
    }

    // ── AC4 · padre que no es cabeza de grupo → rechazado por la base ──────────────────

    [Fact]
    public void AC4_ElTriggerExigeQueElPadreExistaYSeaCabezaDeGrupo()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("SELECT t.is_group_parent, t.parent_tenant_id INTO v_parent_is_group_parent, v_parent_parent_id FROM identity.tenants t WHERE t.id = NEW.parent_tenant_id");
        ddl.Should().Contain("IF NOT FOUND THEN");
        ddl.Should().Contain("IF v_parent_is_group_parent IS NOT TRUE THEN");
    }

    // ── AC7 · el catálogo de tipos de cliente no cambia ────────────────────────────────

    [Fact]
    public void AC7_LaMigracionNoTocaTenantTypeNiSuCheck()
    {
        // Solo sentencias: los COMMENT ON documentan que is_group_parent es independiente de
        // tenant_type, y eso no es tocar la columna ni su CHECK.
        var up = StatementsOnly(MigrationUpSql());
        var down = StatementsOnly(MigrationDownSql());

        up.Should().NotContain("tenant_type");
        up.Should().NotContain("ck_tenants_tenant_type");
        down.Should().NotContain("tenant_type");
        down.Should().NotContain("ck_tenants_tenant_type");
    }

    [Fact]
    public void AC7_IsGroupParentEsIndependienteDelTenantType()
    {
        using var db = NewContext();
        var propiedad = TenantEntity(db).FindProperty(nameof(Tenant.IsGroupParent))!;

        propiedad.ClrType.Should().Be<bool>();
        propiedad.IsNullable.Should().BeFalse();
        propiedad.GetColumnName().Should().Be("is_group_parent");
        propiedad.GetDefaultValue().Should().Be(false);

        // La cabeza de grupo es una columna propia: ninguna sentencia del DDL la ata a tenant_type.
        StatementsOnly(LoadUp()).Should().NotContain("tenant_type");
    }

    // ── AC8 · migración sin backfill, idempotente y reversible ─────────────────────────

    [Fact]
    public void AC8_ElDdlAgregaIsGroupParentNotNullDefaultFalseSinBackfill()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS is_group_parent boolean NOT NULL DEFAULT false");
        Regex.IsMatch(ddl, @"\bUPDATE identity\.tenants\b").Should().BeFalse("ninguna fila existente se modifica");
        Regex.IsMatch(ddl, @"\bINSERT INTO\b").Should().BeFalse("ninguna fila se inserta");
        Regex.IsMatch(ddl, @"\bDELETE FROM\b").Should().BeFalse("ninguna fila se borra");
    }

    [Fact]
    public void AC8_ElUpEsIdempotente()
    {
        var ddl = Normalize(LoadUp());

        Regex.Count(ddl, "ADD COLUMN IF NOT EXISTS").Should().Be(2);
        ddl.Should().Contain("IF NOT EXISTS ( SELECT 1 FROM pg_constraint WHERE conname = 'fk_tenants_parent_tenant')");
        ddl.Should().Contain("IF NOT EXISTS ( SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenants_parent_not_self')");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_tenants_parent_tenant_id");
        ddl.Should().Contain("CREATE OR REPLACE FUNCTION");
        ddl.Should().Contain("DROP TRIGGER IF EXISTS tr_tenants_hierarchy");
    }

    [Fact]
    public void AC8_ElUpDeLaMigracionEsElDdlEmbebido()
    {
        MigrationUpSql().Should().Be(LoadUp());
    }

    [Fact]
    public void AC8_ElDownRevierteTodoEnOrdenInverso()
    {
        var down = Normalize(MigrationDownSql());

        var pasos = new[]
        {
            "DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants",
            "DROP FUNCTION IF EXISTS identity.trg_tenant_hierarchy_depth()",
            "DROP INDEX IF EXISTS identity.ix_tenants_parent_tenant_id",
            "DROP CONSTRAINT IF EXISTS ck_tenants_parent_not_self",
            "DROP CONSTRAINT IF EXISTS fk_tenants_parent_tenant",
            "DROP COLUMN IF EXISTS is_group_parent",
            "DROP COLUMN IF EXISTS parent_tenant_id",
        };

        var posiciones = pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).ToArray();
        posiciones.Should().OnlyContain(i => i >= 0, "el Down debe retirar cada artefacto que creó el Up");
        posiciones.Should().BeInAscendingOrder("trigger → función → índice → constraints → columnas");
    }

    // ── Índice parcial (checklist A9: FK cubierta por índice) ──────────────────────────

    [Fact]
    public void ElDdlCreaElIndiceParcialSobreParentTenantId()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CREATE INDEX IF NOT EXISTS ix_tenants_parent_tenant_id ON identity.tenants (parent_tenant_id) WHERE parent_tenant_id IS NOT NULL");
    }

    [Fact]
    public void ElModeloDeclaraElIndiceParcialSobreParentTenantId()
    {
        using var db = NewContext();

        var indice = TenantEntity(db).GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_tenants_parent_tenant_id");

        indice.IsUnique.Should().BeFalse();
        indice.Properties.Select(p => p.Name).Should().ContainSingle().Which.Should().Be(nameof(Tenant.ParentTenantId));
        indice.GetFilter().Should().Be("parent_tenant_id IS NOT NULL");
    }

    // ── Sin cambio observable: los defaults reproducen el mundo actual ─────────────────

    [Fact]
    public void UnTenantNuevoNaceSinPadreYSinSerCabezaDeGrupo()
    {
        var tenant = new Tenant();

        tenant.ParentTenantId.Should().BeNull();
        tenant.IsGroupParent.Should().BeFalse();
    }
}
