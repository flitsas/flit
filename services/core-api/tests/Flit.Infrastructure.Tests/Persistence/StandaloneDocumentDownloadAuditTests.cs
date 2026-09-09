using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12204 (Feature #12201) — la auditoría de descarga (CF-19) tiene que ser un ÚNICO UPDATE
/// atómico en SQL.
///
/// <para><b>Por qué se inspecciona el código fuente y no se ejecuta el UPDATE:</b> en esta suite no
/// hay Postgres (el repo no tiene Testcontainers ni fixture de base real), y
/// <c>ExecuteUpdateAsync</c> no expone su SQL sin conexión. Un test de comportamiento con doble en
/// memoria demuestra el resultado (dos descargas → 2) pero NO que el incremento viaje en SQL: un
/// <c>SELECT</c> seguido de <c>SaveChanges</c> pasaría igual y perdería una descarga concurrente.
/// Esta comprobación es sobre la sentencia real que se emite, que es justo lo que el AC exige.</para>
///
/// <para>La otra mitad —que ese UPDATE no dispare el trigger de inmutabilidad— se comprueba
/// cruzando las columnas que escribe contra las que el DDL congela.</para>
/// </summary>
public sealed class StandaloneDocumentDownloadAuditTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.105-F12201-generacion-documental.sql";

    /// <summary>Cuerpo del método <c>RegisterDownloadAsync</c> del repositorio.</summary>
    private static string RegisterDownloadSource()
    {
        var source = File.ReadAllText(RepositorySourcePath());
        var start = source.IndexOf("public Task<int> RegisterDownloadAsync", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "el repositorio debe exponer la auditoría de descarga");

        var end = source.IndexOf("private IQueryable<", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    // ── Un solo UPDATE, con el incremento resuelto por el motor ─────────────────────

    [Fact]
    public void ElContadorSeIncrementaConUnaExpresionSobreLaPropiaColumna()
    {
        var metodo = RegisterDownloadSource();

        metodo.Should().MatchRegex(
            @"SetProperty\(\s*x\s*=>\s*x\.DownloadCount\s*,\s*x\s*=>\s*x\.DownloadCount\s*\+\s*1\s*\)",
            "download_count = download_count + 1 lo resuelve el motor; leerlo en C# y escribirlo "
            + "después perdería una descarga concurrente");
    }

    [Fact]
    public void LaAuditoriaEsUnaSolaSentencia()
    {
        var metodo = RegisterDownloadSource();

        Regex.Matches(metodo, @"ExecuteUpdateAsync").Should().HaveCount(1);
        metodo.Should().NotContain("SaveChangesAsync", "un SaveChanges aquí implicaría leer-y-escribir");
        metodo.Should().NotContain("FirstOrDefaultAsync", "no se lee la fila para calcular el contador");
        metodo.Should().NotContain("ToListAsync");
    }

    [Fact]
    public void LaAuditoriaFijaLaFechaEnElMismoUpdateQueElContador()
    {
        var metodo = RegisterDownloadSource();

        metodo.Should().Contain("SetProperty(x => x.DownloadedAt, downloadedAt)",
            "downloaded_at y download_count viajan juntos: el CHECK "
            + "ck_standalone_documents_download_coherente rechaza que se desincronicen");
    }

    // ── El UPDATE respeta la lista blanca del trigger de inmutabilidad ──────────────

    /// <summary>
    /// El trigger congela snapshots, identidad de la fila y —una vez <c>generated</c>— binario,
    /// estado y resumen. Si la auditoría tocara cualquiera de esas columnas, TODA descarga de un
    /// documento generado fallaría con <c>check_violation</c>.
    /// </summary>
    [Theory]
    [InlineData("Status")]
    [InlineData("StoragePath")]
    [InlineData("StorageSha256")]
    [InlineData("SizeBytes")]
    [InlineData("Filename")]
    [InlineData("InputSummary")]
    [InlineData("Scenario")]
    [InlineData("DocumentSnapshot")]
    [InlineData("RuesSnapshot")]
    [InlineData("TenantId")]
    [InlineData("CreatedByUserId")]
    [InlineData("DocumentType")]
    [InlineData("CreatedAt")]
    public void LaAuditoriaNoEscribeNingunaColumnaCongelada(string columna)
    {
        var metodo = RegisterDownloadSource();

        metodo.Should().NotContain($"SetProperty(x => x.{columna}",
            $"{columna} está congelada por tr_standalone_documents_immutable: escribirla aquí "
            + "haría fallar la descarga");
    }

    [Fact]
    public void ElTriggerDelDdlEximeLasColumnasDeLaAuditoria()
    {
        var sql = Ddl();
        var start = sql.IndexOf(
            "CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        var end = sql.IndexOf("LANGUAGE plpgsql", start, StringComparison.Ordinal);
        var cuerpo = sql[start..end];

        cuerpo.Should().NotContain("NEW.downloaded_at",
            "CF-19 escribe DESPUÉS de generated: si el trigger la comparara, la descarga reventaría");
        cuerpo.Should().NotContain("NEW.download_count");
        cuerpo.Should().NotContain("NEW.row_version");
        cuerpo.Should().NotContain("NEW.updated_at");
        cuerpo.Should().NotContain("NEW.updated_by");
    }

    // ── El UPDATE está acotado al tenant y al estado generated ─────────────────────

    [Fact]
    public void LaAuditoriaSoloAlcanzaFilasDelTenantYEnEstadoGenerated()
    {
        var metodo = RegisterDownloadSource();

        metodo.Should().Contain("Scoped(tenantId)",
            "el aislamiento es el WHERE tenant_id del repositorio, no la política RLS");
        metodo.Should().Contain("x.Status == StandaloneDocumentStatus.Generated",
            "no se audita la descarga de un documento que no tiene binario");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────

    private static string Ddl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return Regex.Replace(reader.ReadToEnd(), @"(?m)^\s*--.*$", string.Empty);
    }

    private static string RepositorySourcePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir!.FullName, "services", "core-api", "src", "Flit.Infrastructure", "Persistence",
            "Repositories", "StandaloneDocumentRepository.cs");
    }
}
