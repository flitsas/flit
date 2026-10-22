using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #13265 (Feature #13261, Épica #12741) — «gana quien carga primero» del comprobante de FLITO defendido por el
    /// motor: trigger <c>tr_attachments_flito_gana_primero</c> (lock del trámite + 23505 con
    /// <c>ck_attachments_flito_gana_primero</c>). DDL: <c>131-HU13265-adjunto-flito-gana-el-primero.sql</c>.
    /// </summary>
    public partial class HU13265_AdjuntoFlitoGanaElPrimero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("131-HU13265-adjunto-flito-gana-el-primero.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_attachments_flito_gana_primero ON tramites.procedure_instance_attachments;
                DROP FUNCTION IF EXISTS tramites.trg_attachment_flito_gana_primero();
                DROP FUNCTION IF EXISTS tramites.flito_attachment_tipos();
                """);
    }
}
