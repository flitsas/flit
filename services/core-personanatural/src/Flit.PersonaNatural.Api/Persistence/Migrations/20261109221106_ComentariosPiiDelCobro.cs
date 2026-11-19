using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComentariosPiiDelCobro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "paid_by",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                comment: "@pii:low Identificador del operador de caja que confirmó el pago.",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "method_detail",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "@pii:low Texto libre del cajero cuando el medio de caja es «otro»; no debe llevar datos personales, pero puede.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "closure_email_sent_at",
                schema: "personanatural",
                table: "payment_charge",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Momento en que se reclamó el envío único del correo de cierre; nulo = pendiente (el reclamo se libera si el transporte falla).",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancelled_by",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                comment: "@pii:low Identificador de quien anuló el cobro.",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "@pii:low Motivo de anulación en texto libre; puede traer datos personales.",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "paid_by",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComment: "@pii:low Identificador del operador de caja que confirmó el pago.");

            migrationBuilder.AlterColumn<string>(
                name: "method_detail",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "@pii:low Texto libre del cajero cuando el medio de caja es «otro»; no debe llevar datos personales, pero puede.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "closure_email_sent_at",
                schema: "personanatural",
                table: "payment_charge",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Momento en que se reclamó el envío único del correo de cierre; nulo = pendiente (el reclamo se libera si el transporte falla).");

            migrationBuilder.AlterColumn<string>(
                name: "cancelled_by",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComment: "@pii:low Identificador de quien anuló el cobro.");

            migrationBuilder.AlterColumn<string>(
                name: "cancellation_reason",
                schema: "personanatural",
                table: "payment_charge",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true,
                oldComment: "@pii:low Motivo de anulación en texto libre; puede traer datos personales.");
        }
    }
}
