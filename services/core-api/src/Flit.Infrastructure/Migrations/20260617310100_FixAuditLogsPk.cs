using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAuditLogsPk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM pg_constraint c
                    JOIN pg_class r ON r.oid = c.conrelid
                    JOIN pg_namespace n ON n.oid = r.relnamespace
                    WHERE n.nspname = 'audit' AND r.relname = 'audit_logs' AND c.conname = 'audit_logs_pkey'
                  ) THEN
                    ALTER TABLE audit.audit_logs RENAME CONSTRAINT audit_logs_pkey TO pk_audit_logs;
                  END IF;
                END $$;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM pg_constraint c
                    JOIN pg_class r ON r.oid = c.conrelid
                    JOIN pg_namespace n ON n.oid = r.relnamespace
                    WHERE n.nspname = 'audit' AND r.relname = 'audit_logs' AND c.conname = 'pk_audit_logs'
                  ) THEN
                    ALTER TABLE audit.audit_logs RENAME CONSTRAINT pk_audit_logs TO audit_logs_pkey;
                  END IF;
                END $$;
                """);
    }
}
