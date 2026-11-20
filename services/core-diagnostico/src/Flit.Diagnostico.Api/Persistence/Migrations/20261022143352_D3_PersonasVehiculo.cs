using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class D3_PersonasVehiculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "persona_id",
                schema: "diagnostico",
                table: "intentos_consulta",
                type: "uuid",
                nullable: true,
                comment: "Persona del vehículo consultada (HU #13790); null si la fuente es por vehículo.");

            migrationBuilder.CreateTable(
                name: "personas_vehiculo",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del vehículo."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo al que pertenece la persona."),
                    rol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PROPIETARIO o LOCATARIO."),
                    orden = table.Column<int>(type: "integer", nullable: false, comment: "Posición dentro del rol, desde 1, en el orden del proveedor."),
                    doc_tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, comment: "Tipo de documento (CC, CE, NIT, PAS, TI)."),
                    doc_cifrado = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:high Número de documento cifrado con Data Protection (HU #13751). El nombre no se guarda."),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "PROVEEDOR (RUNT) o DECLARADO_CLIENTE (locatario declarado cuando el proveedor no trae ninguno)."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante en que se registró (cierre de la celda RUNT)."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personas_vehiculo", x => x.id);
                    table.UniqueConstraint("uq_personas_vehiculo_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_personas_vehiculo_declarado_solo_locatario", "origen = 'PROVEEDOR' OR rol = 'LOCATARIO'");
                    table.CheckConstraint("ck_personas_vehiculo_orden", "orden >= 1");
                    table.CheckConstraint("ck_personas_vehiculo_origen", "origen IN ('DECLARADO_CLIENTE', 'PROVEEDOR')");
                    table.CheckConstraint("ck_personas_vehiculo_rol", "rol IN ('PROPIETARIO', 'LOCATARIO')");
                    table.ForeignKey(
                        name: "fk_personas_vehiculo_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Propietarios y locatarios del vehículo con documento cifrado y sin nombres (HU #13790, F05-C3).");

            migrationBuilder.CreateIndex(
                name: "ix_intentos_consulta_persona",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "persona_id" });

            migrationBuilder.CreateIndex(
                name: "ux_personas_vehiculo_rol_orden",
                schema: "diagnostico",
                table: "personas_vehiculo",
                columns: new[] { "tenant_id", "vehiculo_id", "rol", "orden" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_intentos_consulta_personas_vehiculo",
                schema: "diagnostico",
                table: "intentos_consulta",
                columns: new[] { "tenant_id", "persona_id" },
                principalSchema: "diagnostico",
                principalTable: "personas_vehiculo",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_intentos_consulta_personas_vehiculo",
                schema: "diagnostico",
                table: "intentos_consulta");

            migrationBuilder.DropTable(
                name: "personas_vehiculo",
                schema: "diagnostico");

            migrationBuilder.DropIndex(
                name: "ix_intentos_consulta_persona",
                schema: "diagnostico",
                table: "intentos_consulta");

            migrationBuilder.DropColumn(
                name: "persona_id",
                schema: "diagnostico",
                table: "intentos_consulta");
        }
    }
}
