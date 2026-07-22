using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// FEATURE-08 hotfix — elimina el trigger de auditoría incompatible en
    /// <c>tramites.procedure_type_sources</c> (PK compuesta sin columna <c>id</c>).
    /// <c>public.trg_audit_log()</c> asigna <c>NEW.id</c> y rompe INSERT/UPDATE/DELETE,
    /// bloqueando el seed F08 (<c>38-F08-seeds-tipos-configurados.sql</c>) y la radicación
    /// local cuando el schema F08 estaba a medias. Idempotente: <c>DROP TRIGGER IF EXISTS</c>.
    /// </remarks>
    public partial class F08_DropProcedureTypeSourcesAuditTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tr_procedure_type_sources_audit
                    ON tramites.procedure_type_sources;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op a propósito: recrear el trigger volvería a romper la tabla
            // (PK compuesta sin id incompatible con trg_audit_log).
        }
    }
}
