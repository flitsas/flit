using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S3_PropietarioDeclarado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cambio_propietario",
                schema: "diagnostico",
                table: "vehiculos",
                type: "boolean",
                nullable: true,
                comment: "true si el declarado (homologado) no coincide con ningún propietario RUNT (personas_vehiculo rol PROPIETARIO); false si coincide; null sin declarado o sin RUNT cerrado.");

            migrationBuilder.AddColumn<byte[]>(
                name: "propietario_declarado_doc_cifrado",
                schema: "diagnostico",
                table: "vehiculos",
                type: "bytea",
                nullable: true,
                comment: "@pii:high Documento del propietario actual declarado por el cliente, cifrado con Data Protection (mismo IProtectorDocumentoPersona, HU #13751). Nunca en claro ni en logs.");

            migrationBuilder.AddColumn<string>(
                name: "propietario_declarado_doc_tipo",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true,
                comment: "Tipo de documento del propietario actual declarado por el cliente (opcional, Express/Masivo). Solo para compararlo con el propietario RUNT; no alimenta consultas.");

            // HU #14582 (schema.md §2, SP-2): copia de columna a columna, sin descifrar (mismo propósito de Data Protection).
            // a) declarado vigente → propietario declarado; se limpia el locatario (idempotente por el guard).
            migrationBuilder.Sql(
                """
                UPDATE diagnostico.vehiculos
                   SET propietario_declarado_doc_tipo = locatario_doc_tipo,
                       propietario_declarado_doc_cifrado = locatario_doc_cifrado,
                       locatario_doc_tipo = NULL, locatario_doc_cifrado = NULL, locatario_doc_origen = NULL
                 WHERE locatario_doc_origen = 'DECLARADO_CLIENTE' AND propietario_declarado_doc_cifrado IS NULL;
                """);

            // b) respaldo (locatario PROVEEDOR vigente) → propietario declarado.
            migrationBuilder.Sql(
                """
                UPDATE diagnostico.vehiculos
                   SET propietario_declarado_doc_tipo = locatario_declarado_doc_tipo,
                       propietario_declarado_doc_cifrado = locatario_declarado_doc_cifrado
                 WHERE locatario_declarado_doc_cifrado IS NOT NULL AND propietario_declarado_doc_cifrado IS NULL;
                """);

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
                comment: "PROVEEDOR (autollenado de Intempo); DECLARADO_CLIENTE solo en filas anteriores a HU #14582 (S3). Null sin documento.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "PROVEEDOR (autollenado de Intempo) o DECLARADO_CLIENTE (lo declaró el cliente y sigue vigente); null sin documento.");

            migrationBuilder.AlterColumn<string>(
                name: "origen",
                schema: "diagnostico",
                table: "personas_vehiculo",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "PROVEEDOR (RUNT); DECLARADO_CLIENTE solo en filas históricas anteriores a HU #14582 (el declarado ya no entra como persona).",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "PROVEEDOR (RUNT) o DECLARADO_CLIENTE (locatario declarado cuando el proveedor no trae ninguno).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_cambio_propietario",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "cambio_propietario IS NULL OR propietario_declarado_doc_cifrado IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_propietario_declarado",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "(propietario_declarado_doc_tipo IS NULL) = (propietario_declarado_doc_cifrado IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_cambio_propietario",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_propietario_declarado",
                schema: "diagnostico",
                table: "vehiculos");

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

            // HU #14582 (schema.md §2 Down): reconstruye el respaldo (con locatario del proveedor) y el declarado vigente (sin
            // locatario), copiando los mismos bytes. Se pierde cambio_propietario (derivable).
            migrationBuilder.Sql(
                """
                UPDATE diagnostico.vehiculos
                   SET locatario_declarado_doc_tipo = propietario_declarado_doc_tipo,
                       locatario_declarado_doc_cifrado = propietario_declarado_doc_cifrado
                 WHERE propietario_declarado_doc_cifrado IS NOT NULL AND locatario_doc_origen = 'PROVEEDOR' AND locatario_declarado_doc_cifrado IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE diagnostico.vehiculos
                   SET locatario_doc_tipo = propietario_declarado_doc_tipo,
                       locatario_doc_cifrado = propietario_declarado_doc_cifrado,
                       locatario_doc_origen = 'DECLARADO_CLIENTE'
                 WHERE propietario_declarado_doc_cifrado IS NOT NULL AND locatario_doc_origen IS NULL;
                """);

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

            migrationBuilder.DropColumn(
                name: "cambio_propietario",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "propietario_declarado_doc_cifrado",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "propietario_declarado_doc_tipo",
                schema: "diagnostico",
                table: "vehiculos");

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
                oldComment: "PROVEEDOR (autollenado de Intempo); DECLARADO_CLIENTE solo en filas anteriores a HU #14582 (S3). Null sin documento.");

            migrationBuilder.AlterColumn<string>(
                name: "origen",
                schema: "diagnostico",
                table: "personas_vehiculo",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "PROVEEDOR (RUNT) o DECLARADO_CLIENTE (locatario declarado cuando el proveedor no trae ninguno).",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "PROVEEDOR (RUNT); DECLARADO_CLIENTE solo en filas históricas anteriores a HU #14582 (el declarado ya no entra como persona).");
        }
    }
}
