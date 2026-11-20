using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CajaOt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cancelled_by_id",
                schema: "personanatural",
                table: "payment_charge",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancelled_by_name",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirm_idempotency_key",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paid_by_id",
                schema: "personanatural",
                table: "payment_charge",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paid_by_name",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "paid_on_date",
                schema: "personanatural",
                table: "payment_charge",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashier_event",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "@pii:medium Huella HMAC de la IP (no es la IP)."),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_event", x => x.id);
                },
                comment: "Auditoría de la caja del organismo: búsquedas, confirmaciones y anulaciones. Sin datos personales; solo inserción. Alimenta el tope de búsquedas fallidas.");

            migrationBuilder.CreateIndex(
                name: "ix_payment_charge_office_paid_day",
                schema: "personanatural",
                table: "payment_charge",
                columns: new[] { "transit_office_id", "paid_on_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_charge_cancelled_reason",
                schema: "personanatural",
                table: "payment_charge",
                sql: "status <> 'anulado' OR (cancellation_reason IS NOT NULL AND cancelled_by_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_cashier_event_office_created",
                schema: "personanatural",
                table: "cashier_event",
                columns: new[] { "transit_office_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cashier_event_user_created",
                schema: "personanatural",
                table: "cashier_event",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cashier_event",
                schema: "personanatural");

            migrationBuilder.DropIndex(
                name: "ix_payment_charge_office_paid_day",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_charge_cancelled_reason",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "cancelled_by_id",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "cancelled_by_name",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "confirm_idempotency_key",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "paid_by_id",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "paid_by_name",
                schema: "personanatural",
                table: "payment_charge");

            migrationBuilder.DropColumn(
                name: "paid_on_date",
                schema: "personanatural",
                table: "payment_charge");
        }
    }
}
