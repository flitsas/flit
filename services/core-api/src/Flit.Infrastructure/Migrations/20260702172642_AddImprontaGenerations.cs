using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10466 — Improntas · historial de generación (ADR-0022, Feature #10462).
    /// <para>
    /// Tabla append-only SIN RLS (dispensa documentada de A10 en ADR-0022: log de auditoría
    /// global consultado únicamente por SuperAdmin vía <c>SuperAdminPolicy</c>, análogo al
    /// patrón de <c>AnalyticsReadRepository</c> — <c>tenant_id</c> es trazabilidad, no control de
    /// acceso). Sin <c>updated_at/by</c>, <c>deleted_at/by</c> ni <c>row_version</c> (mismo patrón
    /// que <c>admin.ot_api_call_logs</c>), pero sí lleva trigger <c>audit_log</c> por consistencia
    /// con el resto del schema <c>admin</c>.
    /// </para>
    /// <para>
    /// El <c>CreateTable</c>/índices los genera EF Core desde
    /// <c>ImprontaGenerationEntityConfiguration</c>; las FKs a <c>identity.*</c>, el índice
    /// descendente de <c>created_at</c>, los comentarios PII y el trigger de auditoría se agregan
    /// vía SQL crudo (mismo patrón que <c>HU10349_DraftFinalizedAt</c>/<c>HU10350_VigenciaPersisted</c>),
    /// porque EF Core no modela relaciones ni triggers en este repo (ver
    /// <c>TransitOfficeProfileConfiguration</c>).
    /// </para>
    /// </remarks>
    public partial class AddImprontaGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "impronta_generations",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flit_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    radicado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hash_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fecha_impresa = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    num_motor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    num_chasis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    num_serie = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    marca = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    linea = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    modelo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    org_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    org_nit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    org_ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operador = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pdf_content = table.Column<byte[]>(type: "bytea", nullable: false),
                    pdf_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_impronta_generations", x => x.id);
                    table.CheckConstraint("ck_impronta_generations_identificador_vehiculo", "num_motor IS NOT NULL OR num_chasis IS NOT NULL OR num_serie IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "ix_impronta_generations_flit_user_id",
                schema: "admin",
                table: "impronta_generations",
                column: "flit_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_impronta_generations_placa",
                schema: "admin",
                table: "impronta_generations",
                column: "placa");

            migrationBuilder.CreateIndex(
                name: "ix_impronta_generations_tenant_id",
                schema: "admin",
                table: "impronta_generations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_impronta_generations_radicado",
                schema: "admin",
                table: "impronta_generations",
                column: "radicado",
                unique: true);

            // A7/A8: FKs con nombre explícito + ON DELETE/ON UPDATE (no modeladas vía EF fluent en
            // este repo, ver TransitOfficeProfileConfiguration). A9: ya cubiertas por los índices
            // creados arriba (tenant_id, flit_user_id).
            migrationBuilder.Sql(
                """
                ALTER TABLE admin.impronta_generations
                  ADD CONSTRAINT fk_impronta_generations_tenant_id
                  FOREIGN KEY (tenant_id) REFERENCES identity.tenants(id)
                  ON DELETE CASCADE ON UPDATE CASCADE;

                ALTER TABLE admin.impronta_generations
                  ADD CONSTRAINT fk_impronta_generations_flit_user_id
                  FOREIGN KEY (flit_user_id) REFERENCES identity.users(id)
                  ON DELETE RESTRICT ON UPDATE CASCADE;

                -- Índice descendente para el listado por fecha (HU #10468). El scaffolding de EF
                -- Core para HasIndex(...).IsDescending() en una sola columna no genera el flag
                -- correcto (CA1825 + `descending: new bool[0]`), así que se crea vía SQL crudo.
                CREATE INDEX ix_impronta_generations_created_at
                  ON admin.impronta_generations(created_at DESC);

                -- A15: comentarios PII (Ley 1581).
                COMMENT ON COLUMN admin.impronta_generations.placa IS '@pii:medium';
                COMMENT ON COLUMN admin.impronta_generations.num_motor IS '@pii:medium';
                COMMENT ON COLUMN admin.impronta_generations.num_chasis IS '@pii:medium';
                COMMENT ON COLUMN admin.impronta_generations.num_serie IS '@pii:medium';
                COMMENT ON COLUMN admin.impronta_generations.org_nit IS '@pii:low';
                COMMENT ON COLUMN admin.impronta_generations.pdf_content IS '@pii:high';

                -- A10 (dispensa documentada — ADR-0022): SIN RLS. Log de auditoría global
                -- SuperAdmin, análogo a tramites.* en AnalyticsReadRepository. tenant_id es
                -- trazabilidad, no control de acceso; el único gate de acceso es
                -- SuperAdminPolicy en AdminImprontasEndpoints (HU #10467/#10468).

                -- A16: trigger de auditoría (tabla append-only ⇒ sin trigger de row_version).
                DROP TRIGGER IF EXISTS tr_impronta_generations_audit ON admin.impronta_generations;
                CREATE TRIGGER tr_impronta_generations_audit
                  AFTER INSERT OR UPDATE OR DELETE ON admin.impronta_generations
                  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE arrastra automáticamente FKs propias, índices, CHECK, trigger y
            // comentarios de columna (no hay política RLS que limpiar: A10 dispensado). Sin
            // objetos huérfanos tras el rollback.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tr_impronta_generations_audit ON admin.impronta_generations;");

            migrationBuilder.DropTable(
                name: "impronta_generations",
                schema: "admin");
        }
    }
}
