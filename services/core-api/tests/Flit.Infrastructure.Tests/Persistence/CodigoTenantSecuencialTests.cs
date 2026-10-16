using System.Reflection;
using System.Text;
using Flit.Admin.Application.Common;
using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13604 (Feature #13602, Épica #13517) — el código de tenant pasa a ser el consecutivo <c>COMP-XXXX</c>
/// asignado por <c>identity.tenant_code_seq</c>, con recodificación única de los tenants existentes.
/// <para>
/// Igual que <see cref="ConsecutivoGlobalTramiteTests"/>: la suite no tiene Postgres, así que aquí se verifica que el
/// DDL <i>diga</i> lo que debe, que la migración sea descubrible por EF y que el modelo <i>esté declarado</i> como debe.
/// La ejecución contra el motor (AC1 a AC7 de punta a punta) se comprobó sobre una copia de la base de dev y queda
/// en las evidencias de la HU.
/// </para>
/// Uso de ejemplo: <c>dotnet test --filter "FullyQualifiedName~CodigoTenantSecuencialTests"</c>.
/// </summary>
public sealed class CodigoTenantSecuencialTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.136-HU13604-codigo-tenant-secuencial.sql";

    private static string LoadDdl()
    {
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(DdlResource);
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

    /// <summary>
    /// Modelo sin conexión (construirlo no abre la base), con las mismas convenciones que en ejecución
    /// (<see cref="NpgsqlConventions"/>): sin ellas los nombres de columna no salen en snake_case.
    /// </summary>
    private static FlitDbContext NewContext()
    {
        // En staging no existe NpgsqlConventions (HU #13231): se aplica la misma convención snake_case que registra
        // InfrastructureExtensions.
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .UseSnakeCaseNamingConvention();
        return new FlitDbContext(options.Options);
    }

    // ── AC1 — recodificación por antigüedad ──────────────────────────────────

    [Fact]
    public void LaRecodificacionNumeraPorAntiguedadYGuardaElCodigoAnterior()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("row_number() OVER (ORDER BY created_at, id)", "el más antiguo recibe COMP-0001");
        sql.Should().Contain("SET legacy_code = t.code", "el código anterior se conserva para trazabilidad");
        sql.Should().Contain("ADD COLUMN legacy_code varchar(32)");
    }

    [Fact]
    public void LaRecodificacionCubreTodosLosTenantsSinFiltrarPorTipo()
    {
        // Decisión del PO: entran compañías, hijas, concesiones, marcas blancas, FLIT y OT. Un filtro por
        // tenant_type o por transit_office_profiles dejaría tenants con el código viejo.
        var recodificacion = SinComentarios(LoadDdl());
        var bloque = recodificacion[recodificacion.IndexOf("WITH ord AS", StringComparison.Ordinal)..];
        bloque = bloque[..bloque.IndexOf("UPDATE identity.tenants t", StringComparison.Ordinal)];

        bloque.Should().NotContain("WHERE");
        bloque.Should().NotContain("tenant_type");
        bloque.Should().NotContain("transit_office_profiles");
    }

    // ── AC2 — continuidad ────────────────────────────────────────────────────

    [Fact]
    public void LaSecuenciaContinuaDespuesDelUltimoCodigoAsignado()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("CREATE SEQUENCE IF NOT EXISTS identity.tenant_code_seq");
        sql.Should().Contain("setval('identity.tenant_code_seq', v_total + 1, false)",
            "con is_called = false el siguiente nextval devuelve N + 1");
        sql.Should().Contain("ALTER COLUMN code SET DEFAULT identity.fn_next_tenant_code()",
            "el DEFAULT cubre todo camino de inserción (EF, seeders, migrador V1, SQL a mano)");
    }

    // ── AC3 — idempotencia ───────────────────────────────────────────────────

    [Fact]
    public void LaRecodificacionSoloCorreUnaVez()
    {
        var sql = SinComentarios(LoadDdl());

        // El guard es la ausencia de legacy_code, que se crea dentro del mismo bloque.
        var guard = sql.IndexOf("column_name = 'legacy_code'", StringComparison.Ordinal);
        var alta = sql.IndexOf("ADD COLUMN legacy_code", StringComparison.Ordinal);
        var update = sql.IndexOf("UPDATE identity.tenants t", StringComparison.Ordinal);
        var setval = sql.IndexOf("PERFORM setval(", StringComparison.Ordinal);

        guard.Should().BeGreaterThan(0);
        alta.Should().BeGreaterThan(guard, "la columna se crea después de comprobar que no existe");
        update.Should().BeGreaterThan(alta);
        setval.Should().BeGreaterThan(update, "la secuencia solo se ajusta cuando se recodifica");

        sql.Should().NotContain("ADD COLUMN IF NOT EXISTS legacy_code",
            "si la columna se creara fuera del guard, el guard nunca dispararía");
    }

    [Fact]
    public void LosObjetosDelDdlSonReejecutables()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("CREATE SEQUENCE IF NOT EXISTS");
        sql.Should().Contain("CREATE OR REPLACE FUNCTION identity.fn_format_tenant_code(p_n bigint)");
        sql.Should().Contain("CREATE OR REPLACE FUNCTION identity.fn_next_tenant_code()");
    }

    // ── AC5 — sin colisiones ─────────────────────────────────────────────────

    [Fact]
    public void LaRecodificacionUsaDosPasadasParaNoChocarConElUniqueNoDiferible()
    {
        var sql = SinComentarios(LoadDdl());

        sql.Should().Contain("code        = '#' || identity.fn_format_tenant_code(ord.n)",
            "la primera pasada usa un valor intermedio que no puede existir en la tabla");
        sql.Should().Contain("SET code = substr(code, 2)");
        sql.Should().Contain("WHERE code LIKE '#%'");
    }

    [Fact]
    public void ElPrefijoIntermedioNoLoAceptaLaValidacionDeCodigos()
    {
        // La primera pasada solo es segura si ningún código existente empieza por '#'. Los códigos los validó
        // siempre TextFieldPatterns.TenantCode (^[A-Za-z0-9_-]+$): nunca dejó escribir '#'.
        var regex = TextFieldPatterns.TenantCode();

        regex.IsMatch("#COMP-0001").Should().BeFalse();
        regex.IsMatch("COMP-0001").Should().BeTrue("el código nuevo sigue cumpliendo la validación vigente");
    }

    // ── AC6 — formato de 5 dígitos ───────────────────────────────────────────

    [Fact]
    public void ElFormatoRellenaACuatroDigitosYNoTruncaDesde10000()
    {
        var sql = SinComentarios(LoadDdl());

        // lpad sola truncaría: lpad('10000', 4, '0') = '1000'. De ahí el CASE.
        sql.Should().Contain(
            "'COMP-' || CASE WHEN p_n < 10000 THEN lpad(p_n::text, 4, '0') ELSE p_n::text END");
    }

    [Theory]
    [InlineData(1, "COMP-0001")]
    [InlineData(10, "COMP-0010")]
    [InlineData(100, "COMP-0100")]
    [InlineData(1000, "COMP-1000")]
    [InlineData(9999, "COMP-9999")]
    [InlineData(10000, "COMP-10000")]
    public void LaReglaDeFormatoDaLosEjemplosDeLaEpica(long n, string esperado)
    {
        // Espejo en C# de identity.fn_format_tenant_code, con los ejemplos de la épica y el borde de 10000.
        var codigo = "COMP-" + (n < 10000 ? n.ToString("D4") : n.ToString());

        codigo.Should().Be(esperado);
        codigo.Length.Should().BeLessThanOrEqualTo(32, "la columna es varchar(32)");
    }

    // ── AC4 — simulación (contrato del migrador) ─────────────────────────────

    [Fact]
    public void LaMigracionEsDescubriblePorEfYCargaElDdl()
    {
        var tipo = typeof(HU13604_CodigoTenantSecuencial);

        tipo.GetCustomAttribute<MigrationAttribute>()!.Id
            .Should().Be("20261015180000_HU13604_CodigoTenantSecuencial");
        tipo.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<FlitDbContext>();

        using var db = NewContext();
        db.GetService<IMigrationsAssembly>().Migrations.Keys
            .Should().Contain("20261015180000_HU13604_CodigoTenantSecuencial");
    }

    // ── Modelo de EF ─────────────────────────────────────────────────────────

    [Fact]
    public void ElModeloDeclaraElDefaultDelCodigoYNoMapeaLaColumnaLegacy()
    {
        using var db = NewContext();
        var entidad = db.Model.FindEntityType(typeof(Tenant))!;

        var code = entidad.FindProperty(nameof(Tenant.Code))!;
        code.GetDefaultValueSql().Should().Be("identity.fn_next_tenant_code()");
        code.ValueGenerated.Should().Be(ValueGenerated.OnAdd);
        code.GetMaxLength().Should().Be(32);

        // legacy_code es solo trazabilidad en la base. Si EF la mapeara, la incluiría en cada SELECT/INSERT y fallaría
        // contra un esquema anterior a la migración 136 (pruebas de Down/Up de otras migraciones, CI de la HU #13604).
        entidad.GetProperties().Select(p => p.GetColumnName()).Should().NotContain("legacy_code");

        entidad.GetIndexes().Single(i => i.GetDatabaseName() == "uq_tenants_code").IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Hu13605_ElCentinelaDelCodigoEsLaCadenaVacia()
    {
        // La entidad nace con Code = string.Empty. Si el centinela fuera el default de string (null), EF mandaría
        // esa cadena vacía en el INSERT y el DEFAULT de la secuencia nunca se dispararía.
        using var db = NewContext();
        var code = db.Model.FindEntityType(typeof(Tenant))!.FindProperty(nameof(Tenant.Code))!;

        code.Sentinel.Should().Be(string.Empty);
        new Tenant().Code.Should().Be(string.Empty, "el valor inicial de la entidad debe coincidir con el centinela");
    }
}
