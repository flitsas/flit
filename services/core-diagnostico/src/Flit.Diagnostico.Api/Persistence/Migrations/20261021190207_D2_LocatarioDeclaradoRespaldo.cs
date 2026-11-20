using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D2_LocatarioDeclaradoRespaldo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "locatario_doc_origen",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (lo declaró el cliente y sigue vigente); null sin documento.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (respaldo de la captura); null sin documento.");

            migrationBuilder.AddColumn<byte[]>(
                name: "locatario_declarado_doc_cifrado",
                schema: "diagnostico",
                table: "vehiculos",
                type: "bytea",
                nullable: true,
                comment: "@pii:high Documento del locatario declarado por el cliente, cifrado; respaldo cuando el proveedor trae el suyo (R-3).");

            migrationBuilder.AddColumn<string>(
                name: "locatario_declarado_doc_tipo",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                comment: "Tipo del documento del locatario declarado por el cliente que el del proveedor reemplazó (respaldo, R-3).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_locatario_declarado",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "(locatario_declarado_doc_tipo IS NULL) = (locatario_declarado_doc_cifrado IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_locatario_declarado_con_proveedor",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "locatario_declarado_doc_cifrado IS NULL OR locatario_doc_origen = 'PROVEEDOR'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_locatario_declarado",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_locatario_declarado_con_proveedor",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "locatario_declarado_doc_cifrado",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "locatario_declarado_doc_tipo",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.AlterColumn<string>(
                name: "locatario_doc_origen",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (respaldo de la captura); null sin documento.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (lo declaró el cliente y sigue vigente); null sin documento.");
        }
    }
}
