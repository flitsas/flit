using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13073 (Feature #13062) — versión de sincronización en el trámite.
/// <para>
/// Mismo alcance que <see cref="ConsecutivoGlobalTramiteTests"/>: la suite no tiene Postgres, así que
/// aquí se verifica que el DDL <i>diga</i> lo que debe. Que el motor lo ejecute (AC1 a AC3 de punta a
/// punta) lo cubre <c>SyncVersionTramiteMigrationTests</c> en Flit.Integration.Tests, contra Postgres 16.
/// </para>
/// </summary>
public sealed class SyncVersionTramiteTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.122-HU13073-sync-version-tramite.sql";

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

    [Fact]
    public void ElDdlCreaLaSecuenciaGlobalYLasColumnas()
    {
        var ddl = SinComentarios(LoadDdl());

        ddl.Should().Contain("CREATE SEQUENCE IF NOT EXISTS tramites.procedure_sync_seq AS bigint");
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS sync_version    bigint      NOT NULL DEFAULT 0");
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS sync_changed_at timestamptz NOT NULL DEFAULT now()");
        ddl.Should().Contain("OWNED BY tramites.procedure_instances.sync_version");
    }

    [Fact]
    public void ElTriggerCubreAltaYCualquierCambioDelTramite()
    {
        var ddl = SinComentarios(LoadDdl());

        // AC1 y AC2: INSERT y UPDATE, y sin «UPDATE OF columnas», que dejaría cambios fuera.
        ddl.Should().Contain("BEFORE INSERT OR UPDATE ON tramites.procedure_instances");
        ddl.Should().NotContain("UPDATE OF");
        ddl.Should().Contain("FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_sync_stamp()");
    }

    [Fact]
    public void LaVersionSiempreLaAsignaLaSecuencia()
    {
        var ddl = SinComentarios(LoadDdl());

        // AC3: se sobrescribe sin condición; un IF o un COALESCE permitiría fijarla desde fuera.
        ddl.Should().Contain("NEW.sync_version    := nextval('tramites.procedure_sync_seq');");
        ddl.Should().Contain("NEW.sync_changed_at := now();");

        var funcion = ddl[ddl.IndexOf("CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp", StringComparison.Ordinal)..];
        funcion = funcion[..funcion.IndexOf("LANGUAGE plpgsql", StringComparison.Ordinal)];
        funcion.Should().NotContain("IF ").And.NotContain("COALESCE");
    }

    [Fact]
    public void ElDdlEsReaplicable()
    {
        var ddl = SinComentarios(LoadDdl());

        ddl.Should().Contain("DROP TRIGGER IF EXISTS tr_procedure_instances_sync_stamp ON tramites.procedure_instances");
        ddl.Should().Contain("CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp()");
        ddl.Should().NotContain("BEGIN;").And.NotContain("COMMIT;");
    }

    [Fact]
    public void ElModeloDeEfNoMapeaLasColumnasDeSincronizacion()
    {
        // Si EF las mapeara, un INSERT enviaría 0/NULL explícitos y un UPDATE podría incluirlas en
        // el SET; el trigger las pisaría igual, pero nadie debe creer que la aplicación las controla.
        using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=localhost;Database=flit_model_only;Username=none;Password=none")
            .Options);

        var columnas = db.Model.FindEntityType(typeof(ProcedureInstance))!
            .GetProperties()
            .Select(p => p.GetColumnName());

        columnas.Should().NotContain(["sync_version", "sync_changed_at"]);
    }
}
