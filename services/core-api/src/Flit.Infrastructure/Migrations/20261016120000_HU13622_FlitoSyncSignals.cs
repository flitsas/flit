using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13622 (Feature #13616, Épica #13614) — registro coalescente de avisos a FLITO: tabla
/// <c>integrations.flito_sync_signals</c> y el sello de sincronización la llena en la misma transacción.
/// DDL: <c>136-HU13622-flito-sync-signals.sql</c>.
/// Sin cambio de modelo EF (la tabla la usa el despachador por SQL), por eso lleva los atributos inline y no un Designer.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261016120000_HU13622_FlitoSyncSignals")]
public partial class HU13622_FlitoSyncSignals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("136-HU13622-flito-sync-signals.sql"));

    /// <inheritdoc />
    /// <remarks>Devuelve el sello del DDL 123 (sin registro de avisos) y retira la tabla y su función.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'UPDATE' AND OLD.sync_xact = pg_current_xact_id() THEN
                    NEW.sync_version    := OLD.sync_version;
                    NEW.sync_changed_at := OLD.sync_changed_at;
                    NEW.sync_xact       := OLD.sync_xact;
                ELSE
                    NEW.sync_version    := nextval('tramites.procedure_sync_seq');
                    NEW.sync_changed_at := now();
                    NEW.sync_xact       := pg_current_xact_id();
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP FUNCTION IF EXISTS integrations.fn_flito_signal_registrar(tramites.procedure_instances);
            DROP TABLE IF EXISTS integrations.flito_sync_signals;
            """);
}
