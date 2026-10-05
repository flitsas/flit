using System.Text;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #13074 (Feature #13062) — propagación de cambios de las tablas hijas y asignación inicial.
/// <para>
/// Verifica que el DDL <i>diga</i> lo que debe; el comportamiento contra el motor (AC1 a AC4, row_version
/// y auditoría) lo cubre <c>SyncPropagacionHijasMigrationTests</c> en Flit.Integration.Tests, contra
/// Postgres 16.
/// </para>
/// </summary>
public sealed class SyncPropagacionHijasTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.123-HU13074-sync-propagacion-hijas.sql";

    private static string LoadDdl()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return string.Join('\n', reader.ReadToEnd().Split('\n').Select(linea =>
        {
            var corte = linea.IndexOf("--", StringComparison.Ordinal);
            return corte >= 0 ? linea[..corte] : linea;
        }));
    }

    public static TheoryData<string> TablasHijas() =>
    [
        "procedure_instance_actors",
        "procedure_instance_field_values",
        "procedure_instance_status_history",
        "procedure_instance_attachments",
        "procedure_instance_commercial",
    ];

    [Theory]
    [MemberData(nameof(TablasHijas))]
    public void CadaTablaHijaEstaEnLaListaDePropagacion(string tabla) =>
        LoadDdl().Should().Contain($"'{tabla}'");

    [Fact]
    public void LaPropagacionEsPorSentenciaConTransitionTablesYUnTriggerPorEvento()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("AFTER INSERT ON tramites.%I");
        ddl.Should().Contain("AFTER UPDATE ON tramites.%I");
        ddl.Should().Contain("AFTER DELETE ON tramites.%I");
        ddl.Should().Contain("'REFERENCING NEW TABLE AS new_rows FOR EACH STATEMENT '");
        ddl.Should().Contain("'REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows FOR EACH STATEMENT '");
        ddl.Should().Contain("'REFERENCING OLD TABLE AS old_rows FOR EACH STATEMENT '");
    }

    [Fact]
    public void SeSellaUnaVezPorTransaccion()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS sync_xact xid8 NULL");
        ddl.Should().Contain("IF TG_OP = 'UPDATE' AND OLD.sync_xact = pg_current_xact_id() THEN");
        ddl.Should().Contain("AND p.sync_xact IS DISTINCT FROM pg_current_xact_id()");
    }

    [Fact]
    public void RowVersionYAuditoriaDelTramiteIgnoranLosCambiosSoloDeSincronizacion()
    {
        var ddl = LoadDdl();

        ddl.Should().Contain("CREATE TRIGGER tr_procedure_instances_row_version BEFORE UPDATE ON tramites.procedure_instances\n    FOR EACH ROW WHEN (NOT tramites.fn_procedure_instance_solo_sync(OLD, NEW))");
        ddl.Should().Contain("CREATE TRIGGER tr_procedure_instances_audit AFTER INSERT OR DELETE ON tramites.procedure_instances");
        ddl.Should().Contain("CREATE TRIGGER tr_procedure_instances_audit_update AFTER UPDATE ON tramites.procedure_instances\n    FOR EACH ROW WHEN (NOT tramites.fn_procedure_instance_solo_sync(OLD, NEW))");
    }

    [Fact]
    public void LaAsignacionInicialVaEnOrdenDeCreacionSinTriggersYReservandoBloque()
    {
        var ddl = LoadDdl();

        var desactivar = ddl.IndexOf("ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER", StringComparison.Ordinal);
        var activar = ddl.IndexOf("ALTER TABLE tramites.procedure_instances ENABLE TRIGGER USER", StringComparison.Ordinal);
        var numerar = ddl.IndexOf("row_number() OVER (ORDER BY created_at, id)", StringComparison.Ordinal);

        desactivar.Should().BePositive();
        numerar.Should().BeGreaterThan(desactivar).And.BeLessThan(activar);
        ddl.Should().Contain("WHERE sync_version = 0");
        ddl.Should().Contain("PERFORM setval('tramites.procedure_sync_seq', v_base + v_pendientes)");
    }
}
