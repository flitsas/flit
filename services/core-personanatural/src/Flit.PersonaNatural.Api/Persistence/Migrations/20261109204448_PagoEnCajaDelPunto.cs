using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <summary>Feature #14447 (HU #14448): estados pendiente_pago y pagada, y la tabla del cobro de la solicitud del punto.</summary>
    public partial class PagoEnCajaDelPunto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_procedure_request_status",
                schema: "personanatural",
                table: "procedure_request");

            migrationBuilder.CreateTable(
                name: "payment_charge",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    amount_cop = table.Column<long>(type: "bigint", nullable: true),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cash_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    method_detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    invoice_attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lines = table.Column<string>(type: "jsonb", nullable: true),
                    tariff_version = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_charge", x => x.id);
                    table.CheckConstraint("ck_payment_charge_amount", "amount_cop IS NULL OR amount_cop >= 0");
                    table.CheckConstraint("ck_payment_charge_cash_method", "cash_method IS NULL OR cash_method IN ('efectivo', 'datafono', 'transferencia', 'otro')");
                    table.CheckConstraint("ck_payment_charge_method", "method IS NULL OR method IN ('caja', 'sin_cobro')");
                    table.CheckConstraint("ck_payment_charge_paid", "status = 'habilitado' OR (amount_cop IS NOT NULL AND method IS NOT NULL AND paid_at IS NOT NULL)");
                    table.CheckConstraint("ck_payment_charge_status", "status IN ('habilitado', 'pagado', 'anulado')");
                    table.ForeignKey(
                        name: "fk_payment_charge_attachment",
                        column: x => x.invoice_attachment_id,
                        principalSchema: "personanatural",
                        principalTable: "attachment",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_payment_charge_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Cobro de una solicitud del punto: código de cobro, vigencia, valor, medio y quién lo confirmó o anuló. Un solo cobro pagado por solicitud. Sin datos personales.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_procedure_request_status",
                schema: "personanatural",
                table: "procedure_request",
                sql: "status IN ('borrador', 'pendiente_validacion', 'pendiente_pago', 'pagada', 'en_cola', 'creada', 'rechazada_creacion', 'cancelada')");

            migrationBuilder.CreateIndex(
                name: "ix_payment_charge_code",
                schema: "personanatural",
                table: "payment_charge",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_payment_charge_invoice_attachment",
                schema: "personanatural",
                table: "payment_charge",
                column: "invoice_attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_charge_office_paid_at",
                schema: "personanatural",
                table: "payment_charge",
                columns: new[] { "transit_office_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "uq_payment_charge_enabled_per_request",
                schema: "personanatural",
                table: "payment_charge",
                column: "procedure_request_id",
                unique: true,
                filter: "status = 'habilitado'");

            migrationBuilder.CreateIndex(
                name: "uq_payment_charge_paid_per_request",
                schema: "personanatural",
                table: "payment_charge",
                column: "procedure_request_id",
                unique: true,
                filter: "status = 'pagado'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_charge",
                schema: "personanatural");

            migrationBuilder.DropCheckConstraint(
                name: "ck_procedure_request_status",
                schema: "personanatural",
                table: "procedure_request");

            migrationBuilder.AddCheckConstraint(
                name: "ck_procedure_request_status",
                schema: "personanatural",
                table: "procedure_request",
                sql: "status IN ('borrador', 'pendiente_validacion', 'en_cola', 'creada', 'rechazada_creacion', 'cancelada')");
        }
    }
}
