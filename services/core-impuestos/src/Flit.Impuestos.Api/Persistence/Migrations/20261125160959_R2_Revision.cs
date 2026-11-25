using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Impuestos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R2_Revision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "avisos_recibidos",
                schema: "impuestos",
                comment: "Dedupe de avisos por X-Flit-Delivery y por (consulta_id, attempt); sin cuerpo (PII). Purga a los 30 días. Global.",
                oldComment: "Dedupe de avisos por X-Flit-Delivery; sin cuerpo (PII). Purga a los 30 días. Global.");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "impuestos",
                table: "recibos",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AlterColumn<Guid[]>(
                name: "leida_por",
                schema: "impuestos",
                table: "notificaciones",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'",
                comment: "@pii:low Seudónimos (ids de usuario de la plataforma) de quienes leyeron el aviso.",
                oldClrType: typeof(Guid[]),
                oldType: "uuid[]",
                oldDefaultValueSql: "'{}'");

            migrationBuilder.AlterColumn<string>(
                name: "motivo_cierre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "@pii:low Texto libre del usuario; puede contener datos personales",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "creada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Nombre del actor según su token al crear (1.2.0).");

            migrationBuilder.AlterColumn<Guid>(
                name: "creada_por",
                schema: "impuestos",
                table: "liquidaciones",
                type: "uuid",
                nullable: false,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "cerrada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "Nombre del actor según su token al cerrar (1.2.0).");

            migrationBuilder.AlterColumn<Guid>(
                name: "cerrada_por",
                schema: "impuestos",
                table: "liquidaciones",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "secretaria_nombre",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                comment: "@pii:none Nombre de la secretaría de tránsito (Item.secretaria.nombre).",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "placa",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "@pii:low Placa normalizada (S16).",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "Placa normalizada (S16).");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "impuestos",
                table: "historial_aprovisionamiento",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "actualizado_por",
                schema: "impuestos",
                table: "credenciales_empresa",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "impuestos",
                table: "credenciales_empresa",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AlterColumn<Guid>(
                name: "actualizado_por",
                schema: "impuestos",
                table: "configuracion_empresa",
                type: "uuid",
                nullable: true,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "impuestos",
                table: "configuracion_empresa",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "impuestos",
                table: "auditoria_lectura_backoffice",
                type: "uuid",
                nullable: false,
                comment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultima_firma_invalida_en",
                schema: "impuestos",
                table: "aprovisionamientos",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Última firma inválida de un aviso; mide la ventana de una hora del contador.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_cambio_plataforma_en",
                schema: "impuestos",
                table: "aprovisionamientos",
                type: "timestamp with time zone",
                nullable: true,
                comment: "changedAt del último impuestos.producto.cambiado recibido; uno anterior se ignora.");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "impuestos",
                table: "aprovisionamientos",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_liquidacion",
                schema: "impuestos",
                table: "notificaciones",
                column: "liquidacion_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidaciones_config_copia",
                schema: "impuestos",
                table: "liquidaciones",
                sql: "jsonb_typeof(config_copia) = 'object'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_liquidacion_periodos_otros_cargos",
                schema: "impuestos",
                table: "liquidacion_periodos",
                sql: "jsonb_typeof(otros_cargos) = 'array'");

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_items_en_vuelo",
                schema: "impuestos",
                table: "liquidacion_items",
                column: "tenant_id",
                filter: "estado IN ('CONSULTANDO_RUNT','ENVIANDO')");

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_items_pendientes",
                schema: "impuestos",
                table: "liquidacion_items",
                columns: new[] { "tenant_id", "liquidacion_id" },
                filter: "estado = 'PENDIENTE'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fuentes_api_params_schema",
                schema: "impuestos",
                table: "fuentes_api",
                sql: "jsonb_typeof(params_schema) = 'object'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_credenciales_empresa_secreto_anterior",
                schema: "impuestos",
                table: "credenciales_empresa",
                sql: "(webhook_secret_anterior_cifrado IS NULL) = (webhook_secret_anterior_hasta IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_avisos_recibidos_consulta_attempt",
                schema: "impuestos",
                table: "avisos_recibidos",
                columns: new[] { "consulta_id", "attempt" },
                unique: true,
                filter: "consulta_id IS NOT NULL AND attempt IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_notificaciones_liquidaciones",
                schema: "impuestos",
                table: "notificaciones",
                column: "liquidacion_id",
                principalSchema: "impuestos",
                principalTable: "liquidaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_notificaciones_liquidaciones",
                schema: "impuestos",
                table: "notificaciones");

            migrationBuilder.DropIndex(
                name: "ix_notificaciones_liquidacion",
                schema: "impuestos",
                table: "notificaciones");

            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidaciones_config_copia",
                schema: "impuestos",
                table: "liquidaciones");

            migrationBuilder.DropCheckConstraint(
                name: "ck_liquidacion_periodos_otros_cargos",
                schema: "impuestos",
                table: "liquidacion_periodos");

            migrationBuilder.DropIndex(
                name: "ix_liquidacion_items_en_vuelo",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.DropIndex(
                name: "ix_liquidacion_items_pendientes",
                schema: "impuestos",
                table: "liquidacion_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fuentes_api_params_schema",
                schema: "impuestos",
                table: "fuentes_api");

            migrationBuilder.DropCheckConstraint(
                name: "ck_credenciales_empresa_secreto_anterior",
                schema: "impuestos",
                table: "credenciales_empresa");

            migrationBuilder.DropIndex(
                name: "ux_avisos_recibidos_consulta_attempt",
                schema: "impuestos",
                table: "avisos_recibidos");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "impuestos",
                table: "recibos");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "impuestos",
                table: "credenciales_empresa");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "impuestos",
                table: "configuracion_empresa");

            migrationBuilder.DropColumn(
                name: "ultima_firma_invalida_en",
                schema: "impuestos",
                table: "aprovisionamientos");

            migrationBuilder.DropColumn(
                name: "ultimo_cambio_plataforma_en",
                schema: "impuestos",
                table: "aprovisionamientos");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "impuestos",
                table: "aprovisionamientos");

            migrationBuilder.AlterTable(
                name: "avisos_recibidos",
                schema: "impuestos",
                comment: "Dedupe de avisos por X-Flit-Delivery; sin cuerpo (PII). Purga a los 30 días. Global.",
                oldComment: "Dedupe de avisos por X-Flit-Delivery y por (consulta_id, attempt); sin cuerpo (PII). Purga a los 30 días. Global.");

            migrationBuilder.AlterColumn<Guid[]>(
                name: "leida_por",
                schema: "impuestos",
                table: "notificaciones",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'",
                oldClrType: typeof(Guid[]),
                oldType: "uuid[]",
                oldDefaultValueSql: "'{}'",
                oldComment: "@pii:low Seudónimos (ids de usuario de la plataforma) de quienes leyeron el aviso.");

            migrationBuilder.AlterColumn<string>(
                name: "motivo_cierre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true,
                oldComment: "@pii:low Texto libre del usuario; puede contener datos personales");

            migrationBuilder.AlterColumn<string>(
                name: "creada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Nombre del actor según su token al crear (1.2.0).",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)");

            migrationBuilder.AlterColumn<Guid>(
                name: "creada_por",
                schema: "impuestos",
                table: "liquidaciones",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");

            migrationBuilder.AlterColumn<string>(
                name: "cerrada_por_nombre",
                schema: "impuestos",
                table: "liquidaciones",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Nombre del actor según su token al cerrar (1.2.0).",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                oldComment: "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)");

            migrationBuilder.AlterColumn<Guid>(
                name: "cerrada_por",
                schema: "impuestos",
                table: "liquidaciones",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");

            migrationBuilder.AlterColumn<string>(
                name: "secretaria_nombre",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true,
                oldComment: "@pii:none Nombre de la secretaría de tránsito (Item.secretaria.nombre).");

            migrationBuilder.AlterColumn<string>(
                name: "placa",
                schema: "impuestos",
                table: "liquidacion_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "Placa normalizada (S16).",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "@pii:low Placa normalizada (S16).");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "impuestos",
                table: "historial_aprovisionamiento",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");

            migrationBuilder.AlterColumn<Guid>(
                name: "actualizado_por",
                schema: "impuestos",
                table: "credenciales_empresa",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");

            migrationBuilder.AlterColumn<Guid>(
                name: "actualizado_por",
                schema: "impuestos",
                table: "configuracion_empresa",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");

            migrationBuilder.AlterColumn<Guid>(
                name: "actor_id",
                schema: "impuestos",
                table: "auditoria_lectura_backoffice",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "@pii:low Seudónimo del usuario (id de usuario de la plataforma).");
        }
    }
}
