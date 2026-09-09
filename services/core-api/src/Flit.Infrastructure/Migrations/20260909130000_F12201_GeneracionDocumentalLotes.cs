using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #12201 — Incremento I3: carga masiva XLSX. Cabecera de lote
    /// <c>admin.standalone_document_batches</c> + vínculo (<c>batch_id</c>, <c>row_number</c>,
    /// <c>validation_errors</c>) en <c>admin.standalone_documents</c>. Sin tabla de items: una fila
    /// fallida del XLSX es un documento con <c>status='error'</c> (ADR-0056).
    ///
    /// DDL en <c>Persistence/Sql/Ddl/106-F12201-generacion-documental-lotes.sql</c>.
    ///
    /// Va en migración aparte de la 105 —y no ampliando aquel .sql— porque la 105 se aplica al
    /// mergear I1 y una migración ya aplicada no se modifica; además I3 es un incremento con PR
    /// propio. Este archivo debe viajar en el PR de I3, no en el de I1.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260909130000_F12201_GeneracionDocumentalLotes")]
    public partial class F12201_GeneracionDocumentalLotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("106-F12201-generacion-documental-lotes.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte en orden inverso: primero el vínculo en standalone_documents (índices,
            // constraints y columnas), luego la cabecera de lote y, al final, se restaura
            // TEXTUALMENTE la función de inmutabilidad tal como la dejó el DDL 105 — sin las
            // comparaciones de batch_id / row_number, que ya no existirían como columnas.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS admin.ix_standalone_documents_batch_status;
                DROP INDEX IF EXISTS admin.uq_standalone_documents_batch_row;

                ALTER TABLE admin.standalone_documents
                    DROP CONSTRAINT IF EXISTS ck_standalone_documents_batch_row;
                ALTER TABLE admin.standalone_documents
                    DROP CONSTRAINT IF EXISTS fk_standalone_documents_batch;

                ALTER TABLE admin.standalone_documents
                    DROP COLUMN IF EXISTS validation_errors,
                    DROP COLUMN IF EXISTS row_number,
                    DROP COLUMN IF EXISTS batch_id;

                DROP TRIGGER IF EXISTS tr_standalone_document_batches_audit ON admin.standalone_document_batches;
                DROP TRIGGER IF EXISTS tr_standalone_document_batches_row_version ON admin.standalone_document_batches;
                DROP POLICY IF EXISTS tenant_isolation ON admin.standalone_document_batches;

                DROP TABLE IF EXISTS admin.standalone_document_batches;

                CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable() RETURNS trigger AS $$
                BEGIN
                  IF OLD.document_snapshot IS NOT NULL
                     AND NEW.document_snapshot IS DISTINCT FROM OLD.document_snapshot THEN
                    RAISE EXCEPTION 'admin.standalone_documents: document_snapshot es inmutable una vez escrito (id=%)', OLD.id
                      USING ERRCODE = 'check_violation';
                  END IF;

                  IF OLD.rues_snapshot IS NOT NULL
                     AND NEW.rues_snapshot IS DISTINCT FROM OLD.rues_snapshot THEN
                    RAISE EXCEPTION 'admin.standalone_documents: rues_snapshot es inmutable una vez escrito (id=%)', OLD.id
                      USING ERRCODE = 'check_violation';
                  END IF;

                  IF NEW.tenant_id             IS DISTINCT FROM OLD.tenant_id
                     OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
                     OR NEW.document_type      IS DISTINCT FROM OLD.document_type
                     OR NEW.created_at         IS DISTINCT FROM OLD.created_at THEN
                    RAISE EXCEPTION 'admin.standalone_documents: tenant, autor, tipo y fecha de creación son inmutables (id=%)', OLD.id
                      USING ERRCODE = 'check_violation';
                  END IF;

                  IF OLD.status = 'generated' THEN
                    IF NEW.status            IS DISTINCT FROM OLD.status
                       OR NEW.storage_path   IS DISTINCT FROM OLD.storage_path
                       OR NEW.storage_sha256 IS DISTINCT FROM OLD.storage_sha256
                       OR NEW.size_bytes     IS DISTINCT FROM OLD.size_bytes
                       OR NEW.filename       IS DISTINCT FROM OLD.filename
                       OR NEW.input_summary  IS DISTINCT FROM OLD.input_summary
                       OR NEW.scenario       IS DISTINCT FROM OLD.scenario THEN
                      RAISE EXCEPTION 'admin.standalone_documents: estado, binario y resumen son inmutables una vez generated (id=%). Solo se admiten downloaded_at, download_count y el borrado lógico.', OLD.id
                        USING ERRCODE = 'check_violation';
                    END IF;
                  END IF;

                  RETURN NEW;
                END; $$ LANGUAGE plpgsql;
                """);
        }
    }
}
