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
/// HU #12416 (Feature #12368, Épica #12237 Marca Blanca, ADR-0060 D1/D2) — dominio dedicado de la red
/// como dato de la plataforma. Aquí se verifica, SIN conexión, que el DDL 116 <i>diga</i> lo que debe
/// (tabla en <c>admin</c>, columnas estándar, unicidad parcial por red y por host, CHECK de formato
/// RFC 1123 y de estados, disparador fail-closed reutilizado del DDL 115, RLS, triggers de
/// row_version y auditoría, vista de dominios activos), que la migración lo envuelva tal cual, que el
/// <c>Down</c> lo revierta completo y en orden sin tocar la función compartida, que no haya poblado
/// (AC6) y que el modelo EF mapee tabla y vista con nombre explícito. Que el motor rechace de verdad se
/// prueba contra PostgreSQL real en <c>Flit.Integration.Tests/Domains/TenantDomainConstraintsTests</c>.
/// </summary>
public sealed class TenantDomainsSchemaTests
{
    private const string DdlFile = "116-HU12416-tenant-domains.sql";

    private const string HostRegex = @"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+([a-z]{2,63}|xn--[a-z0-9-]{1,59})$";

    private static string LoadUp() => EmbeddedDdl.LoadUp(DdlFile);

    private static string MigrationUpSql() =>
        new E12416_TenantDomains().UpOperations.OfType<SqlOperation>().Single().Sql;

    private static string MigrationDownSql() =>
        new E12416_TenantDomains().DownOperations.OfType<SqlOperation>().Single().Sql;

    private static FlitDbContext NewContext() =>
        new(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

    private static string Normalize(string sql) => Regex.Replace(sql, @"\s+", " ");

    private static string StatementsOnly(string sql)
    {
        // Primero los literales: la regex del CHECK de host contiene "xn--" y un stripper de comentarios
        // ingenuo se comería el resto de la línea (incluida la comilla de cierre).
        var sinLiterales = Regex.Replace(sql, @"'[^']*'", "''");
        return Regex.Replace(sinLiterales, @"--[^\n]*", string.Empty);
    }

    // ── AC1 · registro con formato válido y unicidad ─────────────────────────────────────────

    [Fact]
    public void AC1_ElDdlCreaElDominioEnAdminConHostNormalizadoTokenYEstadoPendiente()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TABLE IF NOT EXISTS admin.tenant_domains (");
        ddl.Should().Contain("id uuid NOT NULL DEFAULT uuidv7()");
        ddl.Should().Contain("host varchar(253) NOT NULL");
        ddl.Should().Contain("status varchar(20) NOT NULL DEFAULT 'pending'");
        ddl.Should().Contain("verification_token varchar(64) NOT NULL");
        ddl.Should().Contain("next_check_at timestamptz NULL");
        ddl.Should().Contain("CONSTRAINT pk_tenant_domains PRIMARY KEY (id)");
        ddl.Should().Contain(
            "CONSTRAINT fk_tenant_domains_tenants FOREIGN KEY (tenant_id) REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT");
    }

    [Fact]
    public void AC1_LaTablaLlevaLasColumnasEstandarDelChecklist()
    {
        var ddl = Normalize(LoadUp());
        var inicio = ddl.IndexOf("CREATE TABLE IF NOT EXISTS admin.tenant_domains (", StringComparison.Ordinal);
        inicio.Should().BeGreaterThanOrEqualTo(0);
        var cuerpo = ddl[inicio..ddl.IndexOf(");", inicio, StringComparison.Ordinal)];

        foreach (var columna in new[]
                 {
                     "tenant_id uuid NOT NULL", "created_at timestamptz NOT NULL DEFAULT now()", "created_by uuid NULL",
                     "updated_at timestamptz NOT NULL DEFAULT now()", "updated_by uuid NULL", "deleted_at timestamptz NULL",
                     "deleted_by uuid NULL", "row_version bigint NOT NULL DEFAULT 0",
                 })
        {
            cuerpo.Should().Contain(columna);
        }
    }

    [Fact]
    public void AC1_ElHostSeExigeEnMinusculasConFormatoRfc1123YLongitudAcotada()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_host_lower CHECK (host = lower(host))");
        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_host_length CHECK (char_length(host) BETWEEN 4 AND 253)");
        ddl.Should().Contain($"CONSTRAINT ck_tenant_domains_host_format CHECK ( host ~ '{HostRegex}' )");
    }

    [Theory]
    [InlineData("red.example.com", true)]
    [InlineData("a-b.example.com", true)]
    [InlineData("xn--espaa-rta.com", true)]
    [InlineData("portal.xn--p1ai", true)]
    [InlineData("Red.Example.com", false)]
    [InlineData("https://red.example.com", false)]
    [InlineData("red.example.com:443", false)]
    [InlineData("red.example.com/x", false)]
    [InlineData("españa.com", false)]
    [InlineData("-red.example.com", false)]
    [InlineData("red-.example.com", false)]
    [InlineData("red..example.com", false)]
    [InlineData("localhost", false)]
    [InlineData("red.example.com.", false)]
    [InlineData("*.example.com", false)]
    [InlineData("1.2.3.4", false)]
    public void AC1_LaExpresionDelCheckDeFormatoAceptaYRechazaLoQueDebe(string host, bool esperado)
    {
        // La misma expresión POSIX del CHECK es válida como .NET regex (sin lookahead ni clases propias
        // de PostgreSQL); el veredicto real del motor lo confirma la prueba de integración.
        var etiquetasOk = host.Split('.').All(l => l.Length is >= 1 and <= 63);

        (Regex.IsMatch(host, HostRegex) && !host.Any(char.IsUpper) && host.Length is >= 4 and <= 253 && etiquetasOk)
            .Should().Be(esperado, host);
    }

    [Fact]
    public void AC1_ElHostYElTokenSonUnicosYElHostSeLiberaAlRetirar()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_host ON admin.tenant_domains (host) WHERE deleted_at IS NULL");
        ddl.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_verification_token ON admin.tenant_domains (verification_token)");
        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_verification_token CHECK (verification_token ~ '^[A-Za-z0-9_-]{16,64}$')");
    }

    // ── AC2 · un dominio por red y sólo para Marca Blanca ────────────────────────────────────

    [Fact]
    public void AC2_UnDominioVigentePorRedYFkCubiertaPorIndice()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_domains_tenant_id ON admin.tenant_domains (tenant_id) WHERE deleted_at IS NULL");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_tenant_domains_tenant_id ON admin.tenant_domains (tenant_id)");
    }

    [Fact]
    public void AC2_ReutilizaLaFuncionCompartidaDelDdl115SinRedefinirla()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CREATE TRIGGER tr_tenant_domains_marca_blanca BEFORE INSERT OR UPDATE OF tenant_id, host ON admin.tenant_domains FOR EACH ROW EXECUTE FUNCTION identity.trg_require_marca_blanca_head('ck_tenant_domains_marca_blanca')");
        ddl.Should().NotContain("CREATE OR REPLACE FUNCTION", "la función es del DDL 115; aquí solo se reutiliza");
        Regex.IsMatch(Normalize(StatementsOnly(LoadUp())), @"CREATE TRIGGER \S+ [^;]* ON identity\.tenants").Should().BeFalse();
        Regex.IsMatch(Normalize(StatementsOnly(LoadUp())), @"ALTER TABLE identity\.tenants").Should().BeFalse();
    }

    // ── AC4/AC5 · estados y resolución: solo active resuelve; retirar/inactivar deja de resolver ─

    [Fact]
    public void AC4_LosEstadosSonCerradosYActivoExigeVerificacionYCertificado()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_status CHECK (status IN ('pending', 'verified', 'active', 'failed'))");
        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_verified_requires_verified_at CHECK ( status NOT IN ('verified', 'active') OR verified_at IS NOT NULL )");
        ddl.Should().Contain(
            "CONSTRAINT ck_tenant_domains_active_requires_verified CHECK ( status <> 'active' OR (verified_at IS NOT NULL AND activated_at IS NOT NULL AND certificate_issued_at IS NOT NULL) )");
        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_failed_requires_reason CHECK ( status <> 'failed' OR (failed_at IS NOT NULL AND failure_reason IS NOT NULL) )");
        ddl.Should().Contain("CONSTRAINT ck_tenant_domains_check_attempts CHECK (check_attempts >= 0)");
    }

    [Fact]
    public void AC4_LaVistaSoloExponeDominiosActivosVigentesDeCabezasMarcaBlancaActivas()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain(
            "CREATE OR REPLACE VIEW admin.v_active_network_domains AS SELECT d.host, d.tenant_id AS head_tenant_id FROM admin.tenant_domains d JOIN identity.tenants t ON t.id = d.tenant_id WHERE d.status = 'active' AND d.deleted_at IS NULL AND t.tenant_type = 'MARCA_BLANCA' AND t.is_group_parent = true AND t.is_active = true");
        ddl.Should().Contain("CREATE INDEX IF NOT EXISTS ix_tenant_domains_host_active ON admin.tenant_domains (host) WHERE status = 'active' AND deleted_at IS NULL");
        ddl.Should().Contain(
            "CREATE INDEX IF NOT EXISTS ix_tenant_domains_next_check ON admin.tenant_domains (next_check_at) WHERE deleted_at IS NULL AND status IN ('pending', 'failed', 'active')");
    }

    // ── AC5 · auditoría, concurrencia y aislamiento ───────────────────────────────────────────

    [Fact]
    public void AC5_LaTablaLlevaRowVersionAuditLogYRls()
    {
        var ddl = Normalize(LoadUp());

        ddl.Should().Contain("CREATE TRIGGER tr_tenant_domains_row_version BEFORE UPDATE ON admin.tenant_domains FOR EACH ROW EXECUTE FUNCTION public.trg_row_version()");
        ddl.Should().Contain("CREATE TRIGGER tr_tenant_domains_audit AFTER INSERT OR UPDATE OR DELETE ON admin.tenant_domains FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log()");
        ddl.Should().Contain("ALTER TABLE admin.tenant_domains ENABLE ROW LEVEL SECURITY");
        ddl.Should().Contain(
            "CREATE POLICY tenant_isolation ON admin.tenant_domains USING ( current_setting('app.is_superadmin', true) = 'true' OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid )");
        ddl.Should().Contain("COMMENT ON COLUMN admin.tenant_domains.verification_token IS '@pii:low");
        Normalize(StatementsOnly(LoadUp())).Should().NotContain("tenant_config_audit_logs", "el old/new legible es responsabilidad del repositorio, no de un trigger");
    }

    // ── AC6 · paridad y migración: sin poblado, idempotente, reversible ───────────────────────

    [Fact]
    public void AC6_ElDdlNoPueblaNingunaFila()
    {
        var ddl = Normalize(StatementsOnly(LoadUp()));

        Regex.IsMatch(ddl, @"\bINSERT INTO\b").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bUPDATE admin\.").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bUPDATE identity\.").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bDELETE FROM\b").Should().BeFalse();
    }

    [Fact]
    public void AC6_ElUpEsIdempotente()
    {
        var ddl = Normalize(LoadUp());

        Regex.Count(ddl, "CREATE TABLE IF NOT EXISTS").Should().Be(1);
        Regex.Count(ddl, "CREATE OR REPLACE VIEW").Should().Be(1);
        Regex.Count(ddl, "CREATE INDEX IF NOT EXISTS|CREATE UNIQUE INDEX IF NOT EXISTS").Should().Be(6);
        Regex.Count(ddl, "DROP TRIGGER IF EXISTS").Should().Be(3, "cada CREATE TRIGGER va precedido de su DROP IF EXISTS");
        Regex.Count(ddl, "CREATE TRIGGER").Should().Be(3);
        Regex.Count(ddl, "DROP POLICY IF EXISTS").Should().Be(1);
        Regex.IsMatch(ddl, @"\bCREATE TABLE (?!IF NOT EXISTS)").Should().BeFalse();
        Regex.IsMatch(ddl, @"\bCREATE VIEW\b").Should().BeFalse();
    }

    [Fact]
    public void AC6_ElUpDeLaMigracionEsElDdlEmbebido()
    {
        MigrationUpSql().Should().Be(LoadUp());
    }

    [Fact]
    public void AC6_ElDownRevierteTodoEnOrdenInversoYNoTocaLaFuncionCompartida()
    {
        var down = Normalize(MigrationDownSql());

        var pasos = new[]
        {
            "DROP VIEW IF EXISTS admin.v_active_network_domains",
            "DROP TRIGGER IF EXISTS tr_tenant_domains_audit ON admin.tenant_domains",
            "DROP TRIGGER IF EXISTS tr_tenant_domains_row_version ON admin.tenant_domains",
            "DROP TRIGGER IF EXISTS tr_tenant_domains_marca_blanca ON admin.tenant_domains",
            "DROP POLICY IF EXISTS tenant_isolation ON admin.tenant_domains",
            "DROP INDEX IF EXISTS admin.ix_tenant_domains_next_check",
            "DROP INDEX IF EXISTS admin.ix_tenant_domains_host_active",
            "DROP INDEX IF EXISTS admin.ix_tenant_domains_tenant_id",
            "DROP INDEX IF EXISTS admin.uq_tenant_domains_verification_token",
            "DROP INDEX IF EXISTS admin.uq_tenant_domains_host",
            "DROP INDEX IF EXISTS admin.uq_tenant_domains_tenant_id",
            "DROP TABLE IF EXISTS admin.tenant_domains",
        };

        var posiciones = pasos.Select(p => down.IndexOf(p, StringComparison.Ordinal)).ToArray();
        posiciones.Should().OnlyContain(i => i >= 0, "el Down debe retirar cada artefacto que creó el Up");
        posiciones.Should().BeInAscendingOrder("vista → triggers → política → índices → tabla");
        down.Should().NotContain("trg_require_marca_blanca_head", "la función es del DDL 115 y la retira su propio Down");
        down.Should().NotContain("identity.tenants");
        down.Should().NotContain("tenant_brandings");
    }

    [Fact]
    public void AC6_LaMigracionSigueALaDe12412YEsDescubiertaPorEf()
    {
        using var db = NewContext();
        var migraciones = db.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToList();

        migraciones.Should().Contain("20260916110000_E12416_TenantDomains");
        migraciones.Should().Contain("20260916100000_E12412_TenantBrandings");
        // Sucesora inmediata de la de #12412 (DDL 115 -> 116), no "la última del ensamblado":
        // cualquier migración posterior mergeada desde develop (p. ej. B12594) no debe romper este AC.
        migraciones[migraciones.IndexOf("20260916100000_E12412_TenantBrandings") + 1]
            .Should().Be("20260916110000_E12416_TenantDomains");
    }

    // ── Modelo EF: tabla excluida de migraciones, vista keyless, concurrencia ─────────────────

    [Fact]
    public void ElModeloMapeaElDominioConColumnasExplicitasYExcluidoDeMigraciones()
    {
        using var db = NewContext();
        var entidad = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TenantDomainEntity))!;

        entidad.GetTableName().Should().Be("tenant_domains");
        entidad.GetSchema().Should().Be("admin");
        entidad.IsTableExcludedFromMigrations().Should().BeTrue();
        entidad.FindPrimaryKey()!.GetName().Should().Be("pk_tenant_domains");
        entidad.FindProperty(nameof(TenantDomainEntity.TenantId))!.GetColumnName().Should().Be("tenant_id");
        entidad.FindProperty(nameof(TenantDomainEntity.Host))!.GetMaxLength().Should().Be(253);
        entidad.FindProperty(nameof(TenantDomainEntity.Status))!.GetMaxLength().Should().Be(20);
        entidad.FindProperty(nameof(TenantDomainEntity.VerificationToken))!.GetColumnName().Should().Be("verification_token");
        entidad.FindProperty(nameof(TenantDomainEntity.CertificateIssuedAt))!.GetColumnName().Should().Be("certificate_issued_at");
        entidad.FindProperty(nameof(TenantDomainEntity.NextCheckAt))!.GetColumnName().Should().Be("next_check_at");
        entidad.FindProperty(nameof(TenantDomainEntity.DeletedAt))!.GetColumnName().Should().Be("deleted_at");
        entidad.FindProperty(nameof(TenantDomainEntity.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entidad.GetIndexes().Select(i => i.GetDatabaseName()).Should().BeEquivalentTo(
        [
            "uq_tenant_domains_tenant_id", "uq_tenant_domains_host", "uq_tenant_domains_verification_token",
            "ix_tenant_domains_tenant_id", "ix_tenant_domains_host_active", "ix_tenant_domains_next_check",
        ]);
        entidad.GetIndexes().Single(i => i.GetDatabaseName() == "uq_tenant_domains_host").GetFilter().Should().Be("deleted_at IS NULL");
        entidad.GetIndexes().Single(i => i.GetDatabaseName() == "uq_tenant_domains_tenant_id").IsUnique.Should().BeTrue();
        entidad.GetDeclaredTriggers().Select(t => t.GetDatabaseName()).Should().BeEquivalentTo(
            ["tr_tenant_domains_marca_blanca", "tr_tenant_domains_row_version", "tr_tenant_domains_audit"]);
    }

    [Fact]
    public void ElModeloMapeaLaVistaComoKeylessSinTablaNiMigracion()
    {
        using var db = NewContext();
        var entidad = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ActiveNetworkDomainView))!;

        entidad.FindPrimaryKey().Should().BeNull("vista keyless");
        entidad.GetTableName().Should().BeNull("una vista no es tabla: nada que migrar");
        entidad.GetViewName().Should().Be("v_active_network_domains");
        entidad.GetViewSchema().Should().Be("admin");
        entidad.FindProperty(nameof(ActiveNetworkDomainView.Host))!.GetColumnName().Should().Be("host");
        entidad.FindProperty(nameof(ActiveNetworkDomainView.HeadTenantId))!.GetColumnName().Should().Be("head_tenant_id");
    }
}
