using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #11461 (Feature #11459) — verificación estática del DDL 69 de la cola de despachos.
/// Sin Postgres en la suite: comprueba que el script dice lo que debe decir.
/// </summary>
public sealed class ProcedureStateChangeEmailDispatchSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.69-tramite-state-email-dispatch.sql";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Statements() =>
        Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    [Fact]
    public void UnMismoBuzonNoPuedeRepetirseDentroDelMismoEvento()
    {
        var sql = Load();

        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_psce_dispatches_outbox_recipient");
        sql.Should().MatchRegex(
            @"ON tramites\.procedure_state_change_email_dispatches\s*\(outbox_id,\s*lower\(recipient\)\)\s*WHERE recipient IS NOT NULL");
        sql.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS uq_psce_dispatches_outbox_gap");
        sql.Should().MatchRegex(
            @"ON tramites\.procedure_state_change_email_dispatches\s*\(outbox_id,\s*recipient_role,\s*recipient_kind\)\s*WHERE recipient IS NULL");
    }

    [Fact]
    public void UnEventoPuedeRegistrarHastaCuatroCuposVacios()
    {
        // No debe existir UNIQUE cuya única columna indexada sea outbox_id.
        var sql = Statements();
        sql.Should().NotMatchRegex(
            @"CREATE UNIQUE INDEX[^\n]*\n?\s*ON tramites\.procedure_state_change_email_dispatches\s*\(\s*outbox_id\s*\)");
    }

    [Fact]
    public void LaTablaNaceConAislamientoPorTenantYDestinatarioClasificado()
    {
        var sql = Load();

        sql.Should().Contain("recipient_role");
        sql.Should().Contain("recipient_kind");
        sql.Should().Contain(
            "ALTER TABLE tramites.procedure_state_change_email_dispatches ENABLE ROW LEVEL SECURITY");
        sql.Should().Contain(
            "CREATE POLICY tenant_isolation ON tramites.procedure_state_change_email_dispatches");

        var comentario = Regex.Match(
            sql,
            @"COMMENT ON COLUMN tramites\.procedure_state_change_email_dispatches\.recipient IS\s*'([^']*)'");
        comentario.Success.Should().BeTrue("recipient debe llevar COMMENT ON COLUMN");
        comentario.Groups[1].Value.Should().Contain("@pii:");
        comentario.Groups[1].Value.Should().Contain("trazabilidad");
    }

    [Fact]
    public void ReaplicarLaMigracionNoRompeNiDuplica()
    {
        var sql = Statements();

        sql.Should().NotMatchRegex(@"CREATE TABLE (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"CREATE (UNIQUE )?INDEX (?!IF NOT EXISTS)");
        sql.Should().NotMatchRegex(@"ADD COLUMN (?!IF NOT EXISTS)");
        sql.Should().Contain("DROP POLICY IF EXISTS tenant_isolation");
    }

    [Fact]
    public void ColaDePendientesIndexadaPorQueuedAt()
    {
        Load().Should().MatchRegex(
            @"CREATE INDEX IF NOT EXISTS ix_psce_dispatches_pending_queued_at\s*\r?\n?\s*ON tramites\.procedure_state_change_email_dispatches \(queued_at\)\s*\r?\n?\s*WHERE status = 'pendiente'");
    }

    [Fact]
    public void FkAlOutboxConCascade()
    {
        Load().Should().Contain(
            "REFERENCES tramites.procedure_state_change_outbox(id) ON DELETE CASCADE");
    }
}
