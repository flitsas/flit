using System.Text.RegularExpressions;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Sql;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12412 (Feature #12366, Épica #12237 Marca Blanca, ADR-0060 D1) — identidad de marca de la cabeza
/// de red. Aquí se verifica, SIN conexión, que el DDL 115 <i>diga</i> lo que debe (tablas en
/// <c>admin</c>, columnas estándar, disparador fail-closed de tipo MARCA_BLANCA, RLS, triggers de
/// row_version y auditoría, CHECKs de coherencia de publicación y del logotipo), que la migración lo
/// envuelva tal cual, que el <c>Down</c> lo revierta completo y en orden, que no haya poblado (AC8) y
/// que el modelo EF mapee las columnas con nombre explícito. Que el motor rechace de verdad se prueba
/// contra PostgreSQL real en <c>Flit.Integration.Tests/Branding/TenantBrandingConstraintsTests</c>.
/// </summary>
public sealed class TenantBrandingsSchemaTests
{
    private const string DdlFile = "115-HU12412-tenant-brandings.sql";

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new E12412_TenantBrandings().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new E12412_TenantBrandings().DownOperations.OfType<SqlOperation>().Single().Sql;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    private static string StatementsOnly(string sql)
    {
        var sinComentarios = Regex.Replace(sql, @"--[^\n]*", string.Empty);
        return Regex.Replace(sinComentarios, @"'[^']*'", "''");
    }

    // ── AC1 · la marca es un dato de la plataforma: borrador + publicada, 1:1 con la cabeza ──

    [Fact]
    public void AC1_ElDdlCreaLaMarcaEnAdminConBorradorYPublicadaYUnaFilaPorCabeza()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS admin.tenant_brandings (");
        ddl.Should().Contain("id uuid NOT NULL DEFAULT uuidv7()");
        ddl.Should().Contain("draft jsonb NOT NULL DEFAULT '{\"schemaVersion\":1}'::jsonb");
        ddl.Should().Contain("published jsonb NULL");
        ddl.Should().Contain("published_version integer NOT NULL DEFAULT 0");
        ddl.Should().Contain("CONSTRAINT pk_tenant_brandings PRIMARY KEY (id)");
        ddl.Should().Contain("CONSTRAINT uq_tenant_brandings_tenant_id UNIQUE (tenant_id)");
        ddl.Should().Contain(
            "CONSTRAINT fk_tenant_brandings_tenants FOREIGN KEY (tenant_id) REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT");
    }

    [Fact]
    public void AC1_LasDosTablasLlevanLasColumnasEstandarDelChecklist()
    {
        var ddl = Normalize(LoadUp());

        foreach (var tabla in new[] { "admin.tenant_brandings", "admin.tenant_brand_logos" })
        {
            var inicio = ddl.IndexOf($"CREATE TABLE IF NOT EXISTS {tabla} (", StringComparison.Ordinal);
            inicio.Should().BeGreaterThanOrEqualTo(0);
            var cuerpo = ddl[inicio..ddl.IndexOf(");", inicio, StringComparison.Ordinal)];

            foreach (var columna in new[]
                     {
                         "tenant_id uuid NOT NULL", "created_at timestamptz NOT NULL DEFAULT now()", "created_by uuid NULL",
                         "updated_at timestamptz NOT NULL DEFAULT now()", "updated_by uuid NULL", "deleted_at timestamptz NULL",
                         "deleted_by uuid NULL", "row_version bigint NOT NULL DEFAULT 0",
                     })
            {
                cuerpo.Should().Contain(columna, $"{tabla} debe llevar {columna}");
            }
        }
    }

    // ── AC2 · solo una cabeza MARCA_BLANCA tiene marca: el motor también lo rechaza ─────────

    [Fact]
    public void AC2_LaFuncionCompartidaExigeCabezaMarcaBlancaYReportaCheckViolationConNombreDeConstraint()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE OR REPLACE FUNCTION identity.trg_require_marca_blanca_head() RETURNS trigger");
        ddl.Should().Contain("IF v_type IS DISTINCT FROM 'MARCA_BLANCA' OR v_parent IS DISTINCT FROM true THEN");
        ddl.Should().Contain("USING ERRCODE = 'check_violation', CONSTRAINT = TG_ARGV[0], TABLE = TG_TABLE_NAME, SCHEMA = TG_TABLE_SCHEMA");
        StatementsOnly(LoadUp()).Should().NotContain("NEW.tenant_id :=", "fail-closed: nadie corrige el tenant en silencio");
    }

    [Fact]
    public void AC2_AmbasTablasDisparanLaFuncionAntesDeInsertar()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenant_brandings_marca_blanca BEFORE INSERT OR UPDATE OF tenant_id, draft, published ON admin.tenant_brandings FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_brandings_marca_blanca')");
        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenant_brand_logos_marca_blanca BEFORE INSERT OR UPDATE OF tenant_id ON admin.tenant_brand_logos FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_brand_logos_marca_blanca')");
    }

    [Fact]
    public void AC2_NoHayDisparadorSobreIdentityTenants_ElDatoSeConservaAlCambiarElTipo()
    {
        // AC6: cambiar el tipo de la cabeza no borra ni cascadea; los resolutores comprueban el tipo en lectura.
        var ddl = Normalize(StatementsOnly(LoadUp()));

        Regex.IsMatch(ddl, @"CREATE TRIGGER \S+ [^;]* ON identity\.tenants").Should().BeFalse();
        Regex.IsMatch(ddl, @"ALTER TABLE identity\.tenants").Should().BeFalse();
        ddl.Should().NotContain("ON DELETE CASCADE");
    }

    // ── AC3 · aislamiento: RLS con la política del repo ────────────────────────────────────

    [Fact]
    public void AC3_AmbasTablasTienenRlsConLaPoliticaTenantIsolation()
    {
        var ddl = Normalize(LoadUp());

        foreach (var tabla in new[] { "admin.tenant_brandings", "admin.tenant_brand_logos" })
        {
            ddl.Should().Contain($"ALTER TABLE {tabla} ENABLE ROW LEVEL SECURITY");
            ddl.Should().Contain($"DROP POLICY IF EXISTS tenant_isolation ON {tabla}");
            ddl.Should().Contain(
                $"CREATE POLICY tenant_isolation ON {tabla} USING ( current_setting('app.is_superadmin', true) = 'true' OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid )");
        }
    }

    // ── AC4 · publicación coherente: published, published_at, published_by y version van juntos ──

    [Fact]
    public void AC4_ElCheckDePublicacionAcoplaSnapshotInstanteAutorYVersion()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CONSTRAINT ck_tenant_brandings_published_consistent CHECK ( (published IS NULL AND published_at IS NULL AND published_by IS NULL AND published_version = 0) OR (published IS NOT NULL AND published_at IS NOT NULL AND published_by IS NOT NULL AND published_version > 0) )");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brandings_draft_object CHECK (jsonb_typeof(draft) = 'object')");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brandings_published_object CHECK (published IS NULL OR jsonb_typeof(published) = 'object')");
    }

    // ── AC5 · auditoría: rastro técnico por trigger; el old/new legible lo escribe el repositorio ──

    [Fact]
    public void AC5_AmbasTablasLlevanRowVersionYAuditLog()
    {
        var ddl = Normalize(LoadUp());

        foreach (var tabla in new[] { "tenant_brandings", "tenant_brand_logos" })
        {
            ddl.Should().Contain(
                $"CREATE TRIGGER tr_{tabla}_row_version BEFORE UPDATE ON admin.{tabla} FOR EACH ROW EXECUTE FUNCTION public.trg_row_version()");
            ddl.Should().Contain(
                $"CREATE TRIGGER tr_{tabla}_audit AFTER INSERT OR UPDATE OR DELETE ON admin.{tabla} FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log()");
        }

        // public.trg_audit_log() lee NEW.id: por eso la marca lleva id uuidv7 y no tenant_id como PK.
        ddl.Should().NotContain("PRIMARY KEY (tenant_id)");
        Normalize(StatementsOnly(LoadUp())).Should().NotContain("tenant_config_audit_logs", "el old/new legible es responsabilidad del repositorio, no de un trigger");
    }

    // ── AC7 · logotipo versionado: integridad, una sola activa, la anterior se conserva ─────

    [Fact]
    public void AC7_ElLogotipoSeVersionaConIntegridadYUnaSolaVersionActivaPorCabeza()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS admin.tenant_brand_logos (");
        ddl.Should().Contain("CONSTRAINT uq_tenant_brand_logos_tenant_version UNIQUE (tenant_id, version)");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brand_logos_version CHECK (version > 0)");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brand_logos_status CHECK (status IN ('active', 'superseded'))");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brand_logos_content_type CHECK (content_type IN ('image/png', 'image/jpeg', 'image/webp'))");
        ddl.Should().Contain("CONSTRAINT ck_tenant_brand_logos_sha CHECK (storage_sha256 ~ '^[0-9a-f]{64}$')");
        ddl.Should().Contain(
            "CONSTRAINT ck_tenant_brand_logos_superseded CHECK ( (status = 'active' AND superseded_at IS NULL) OR (status = 'superseded' AND superseded_at IS NOT NULL) )");
        ddl.Should().Contain(
            "CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_brand_logos_one_active ON admin.tenant_brand_logos (tenant_id) WHERE status = 'active' AND deleted_at IS NULL");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_tenant_brand_logos_tenant_id ON admin.tenant_brand_logos (tenant_id)");
        ddl.Should().Contain("COMMENT ON COLUMN admin.tenant_brand_logos.filename IS '@pii:low");
    }

    // ── AC8 · paridad y migración: sin poblado, idempotente, reversible ─────────────────────

    [Fact]
    public void AC8_ElDdlNoPueblaNingunaFila()
    {
        var ddl = Normalize(StatementsOnly(LoadUp()));

        Regex.IsMatch(ddl, @"\bINSERT INTO\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bUPDATE admin\.").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bUPDATE identity\.").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bDELETE FROM\b").Should().BeFalse();
    }

    [Fact]
    public void AC8_ElUpEsIdempotente()
    {
        var ddl = Normalize(LoadUp());

        Regex.Count(ddl, "CREATE TABLE IF NOT EXISTS").Should().Be(2);
        Regex.Count(ddl, "CREATE OR REPLACE FUNCTION").Should().Be(1);
        Regex.Count(ddl, "CREATE INDEX IF NOT EXISTS|CREATE UNIQUE INDEX IF NOT EXISTS").Should().Be(2);
        Regex.Count(ddl, "DROP TRIGGER IF EXISTS").Should().Be(6, "cada CREATE TRIGGER va precedido de su DROP IF EXISTS");
        Regex.Count(ddl, "CREATE TRIGGER").Should().Be(6);
        Regex.Count(ddl, "DROP POLICY IF EXISTS").Should().Be(2);
        Regex.IsMatch(ddl, @"\bCREATE TABLE (?!IF NOT EXISTS)").Should().BeFalse();
    }

    [Fact]
    public void AC8_ElUpDeLaMigracionEsElDdlEmbebido()
    {
        MigrationUpSql().Should().Be(LoadUp());
    }

    [Fact]
    public void AC8_ElDownRevierteTodoEnOrdenInversoYRetiraLaFuncionAlFinal()
    {
        var down = Normalize(MigrationDownSql());

        var pasos = new[]
        {
            "DROP TRIGGER IF EXISTS tr_tenant_brand_logos_audit ON admin.tenant_brand_logos",
            "DROP TRIGGER IF EXISTS tr_tenant_brand_logos_row_version ON admin.tenant_brand_logos",
            "DROP TRIGGER IF EXISTS tr_tenant_brand_logos_marca_blanca ON admin.tenant_brand_logos",
            "DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brand_logos",
            "DROP TABLE IF EXISTS admin.tenant_brand_logos",
            "DROP TRIGGER IF EXISTS tr_tenant_brandings_audit ON admin.tenant_brandings",
            "DROP TRIGGER IF EXISTS tr_tenant_brandings_row_version ON admin.tenant_brandings",
            "DROP TRIGGER IF EXISTS tr_tenant_brandings_marca_blanca ON admin.tenant_brandings",
            "DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_brandings",
            "DROP TABLE IF EXISTS admin.tenant_brandings",
            "DROP FUNCTION IF EXISTS identity.trg_require_marca_blanca_head()",
        };

        var posiciones = pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).ToArray();
        posiciones.Should().OnlyContain(i => i >= 0, "el Down debe retirar cada artefacto que creó el Up");
        posiciones.Should().BeInAscendingOrder("logos → marca → función compartida (la reutiliza el DDL 116)");
        down.Should().NotContain("identity.tenants", "el Down no toca la tabla de clientes");
    }

    [Fact]
    public void AC8_LaMigracionSigueALaDe12543YEsDescubiertaPorEf()
    {
        // GetMigrations enumera las migraciones del ensamblado sin abrir conexión: si no aparece,
        // db.Database.Migrate() nunca la aplicaría (gotcha documentado del repo).
        using var db = NewContext();
        var migraciones = db.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToList();

        migraciones.Should().Contain("20260916100000_E12412_TenantBrandings");
        migraciones.Last().Should().Be("20260916100000_E12412_TenantBrandings");
        migraciones.Should().Contain("20260914200000_E12543_ProcedureTermsAcceptances");
    }

    // ── Modelo EF: mapeo explícito, ExcludeFromMigrations, concurrencia ──────────────────────

    [Fact]
    public void ElModeloMapeaLaMarcaConColumnasExplicitasYExcluidaDeMigraciones()
    {
        using var db = NewContext();
        // ExcludeFromMigrations solo vive en el modelo de diseño (no en el optimizado de lectura).
        var entidad = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TenantBrandingEntity))!;

        entidad.GetTableName().Should().Be("tenant_brandings");
        entidad.GetSchema().Should().Be("admin");
        entidad.IsTableExcludedFromMigrations().Should().BeTrue();
        entidad.FindPrimaryKey()!.GetName().Should().Be("pk_tenant_brandings");
        entidad.FindProperty(nameof(TenantBrandingEntity.TenantId))!.GetColumnName().Should().Be("tenant_id");
        entidad.FindProperty(nameof(TenantBrandingEntity.Draft))!.GetColumnType().Should().Be("jsonb");
        entidad.FindProperty(nameof(TenantBrandingEntity.Published))!.GetColumnType().Should().Be("jsonb");
        entidad.FindProperty(nameof(TenantBrandingEntity.PublishedVersion))!.GetColumnName().Should().Be("published_version");
        entidad.FindProperty(nameof(TenantBrandingEntity.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entidad.FindProperty(nameof(TenantBrandingEntity.DeletedAt))!.GetColumnName().Should().Be("deleted_at");
        entidad.GetIndexes().Should().ContainSingle(i => i.GetDatabaseName() == "uq_tenant_brandings_tenant_id" && i.IsUnique);
        entidad.GetDeclaredTriggers().Select(t => t.GetDatabaseName()).Should().BeEquivalentTo(
            ["tr_tenant_brandings_marca_blanca", "tr_tenant_brandings_row_version", "tr_tenant_brandings_audit"]);
    }

    [Fact]
    public void ElModeloMapeaElLogotipoConColumnasExplicitasYExcluidoDeMigraciones()
    {
        using var db = NewContext();
        var entidad = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TenantBrandLogoEntity))!;

        entidad.GetTableName().Should().Be("tenant_brand_logos");
        entidad.GetSchema().Should().Be("admin");
        entidad.IsTableExcludedFromMigrations().Should().BeTrue();
        entidad.FindPrimaryKey()!.GetName().Should().Be("pk_tenant_brand_logos");
        entidad.FindProperty(nameof(TenantBrandLogoEntity.StorageSha256))!.GetColumnType().Should().Be("character(64)");
        entidad.FindProperty(nameof(TenantBrandLogoEntity.ContentType))!.GetMaxLength().Should().Be(20);
        entidad.FindProperty(nameof(TenantBrandLogoEntity.WidthPx))!.GetColumnName().Should().Be("width_px");
        entidad.FindProperty(nameof(TenantBrandLogoEntity.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entidad.GetIndexes().Select(i => i.GetDatabaseName()).Should().BeEquivalentTo(
            ["uq_tenant_brand_logos_tenant_version", "uq_tenant_brand_logos_one_active", "ix_tenant_brand_logos_tenant_id"]);
        entidad.GetIndexes().Single(i => i.GetDatabaseName() == "uq_tenant_brand_logos_one_active").GetFilter()
            .Should().Be("status = 'active' AND deleted_at IS NULL");
    }
}
