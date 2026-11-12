using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4d_DerivacionAjustesRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "reintentado_por",
                schema: "diagnostico",
                table: "derivaciones",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Usuario que reabrió el ítem con «reintentar fallidas» (claim sub, seudónimo); autor del borrador reintentado. Null si nunca se reintentó.");

            migrationBuilder.AlterColumn<Guid>(
                name: "usuario_id",
                schema: "diagnostico",
                table: "derivacion_operaciones",
                type: "uuid",
                nullable: false,
                comment: "@pii:low Usuario que lanzó la operación (claim sub, seudónimo); autor de los borradores (4.6).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Usuario que lanzó la operación (sub); autor de los borradores (4.6). Seudónimo uuid.");

            migrationBuilder.CreateIndex(
                name: "ix_derivaciones_vehiculo",
                schema: "diagnostico",
                table: "derivaciones",
                columns: new[] { "tenant_id", "vehiculo_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_derivaciones_vehiculo",
                schema: "diagnostico",
                table: "derivaciones");

            migrationBuilder.DropColumn(
                name: "reintentado_por",
                schema: "diagnostico",
                table: "derivaciones");

            migrationBuilder.AlterColumn<Guid>(
                name: "usuario_id",
                schema: "diagnostico",
                table: "derivacion_operaciones",
                type: "uuid",
                nullable: false,
                comment: "Usuario que lanzó la operación (sub); autor de los borradores (4.6). Seudónimo uuid.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "@pii:low Usuario que lanzó la operación (claim sub, seudónimo); autor de los borradores (4.6).");
        }
    }
}
