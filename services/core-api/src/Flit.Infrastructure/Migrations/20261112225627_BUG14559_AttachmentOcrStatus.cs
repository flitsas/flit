using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Bug #14559 (ADR-0085) — registra en el adjunto el veredicto OCR declarado por el cliente
    /// (<c>tramites.procedure_instance_attachments.ocr_status</c> y <c>ocr_reason</c>, códigos cerrados con CHECK), para que
    /// el wizard lo rehidrate al cambiar de paso. Informativo, nunca compuerta. Aditiva, nullable, idempotente y SIN backfill
    /// (los adjuntos previos quedan en NULL). DDL: <c>196-BUG14559-attachment-ocr-status.sql</c>.
    /// </summary>
    public partial class BUG14559_AttachmentOcrStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("196-BUG14559-attachment-ocr-status.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instance_attachments
                    DROP CONSTRAINT IF EXISTS ck_procedure_instance_attachments_ocr_reason,
                    DROP CONSTRAINT IF EXISTS ck_procedure_instance_attachments_ocr_status;
                ALTER TABLE tramites.procedure_instance_attachments
                    DROP COLUMN IF EXISTS ocr_reason,
                    DROP COLUMN IF EXISTS ocr_status;
                """);
    }
}
