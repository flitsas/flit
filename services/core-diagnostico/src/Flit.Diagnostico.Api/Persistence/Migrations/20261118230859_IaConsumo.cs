using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IaConsumo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ia_consumo_log",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intento = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    pasada = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latencia_ms = table.Column<int>(type: "integer", nullable: false),
                    aplicativo = table.Column<string>(type: "text", nullable: false),
                    servicio = table.Column<string>(type: "text", nullable: false),
                    ambiente = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<string>(type: "text", nullable: true),
                    usuario_id = table.Column<string>(type: "text", nullable: true),
                    usuario_login = table.Column<string>(type: "text", nullable: true),
                    origen = table.Column<string>(type: "text", nullable: false),
                    modulo = table.Column<string>(type: "text", nullable: false),
                    proceso = table.Column<string>(type: "text", nullable: false),
                    tipo_tarea = table.Column<string>(type: "text", nullable: false),
                    placa = table.Column<string>(type: "text", nullable: true),
                    entidad_tipo = table.Column<string>(type: "text", nullable: true),
                    entidad_id = table.Column<string>(type: "text", nullable: true),
                    archivo_mime = table.Column<string>(type: "text", nullable: true),
                    archivo_bytes = table.Column<long>(type: "bigint", nullable: true),
                    archivo_paginas = table.Column<int>(type: "integer", nullable: true),
                    archivo_sha256 = table.Column<string>(type: "text", nullable: true),
                    proveedor = table.Column<string>(type: "text", nullable: false),
                    modelo_solicitado = table.Column<string>(type: "text", nullable: true),
                    modelo_respondido = table.Column<string>(type: "text", nullable: true),
                    id_respuesta_proveedor = table.Column<string>(type: "text", nullable: true),
                    max_tokens = table.Column<int>(type: "integer", nullable: true),
                    prompt_codigo = table.Column<string>(type: "text", nullable: true),
                    prompt_version = table.Column<string>(type: "text", nullable: true),
                    prompt_hash = table.Column<string>(type: "text", nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    stop_reason = table.Column<string>(type: "text", nullable: true),
                    error_codigo = table.Column<string>(type: "text", nullable: true),
                    error_mensaje = table.Column<string>(type: "text", nullable: true),
                    tokens_entrada = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tokens_salida = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tokens_cache_lectura = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    tokens_cache_escritura = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    costo_usd_estimado = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    metadatos = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ia_consumo_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ia_tarifa_modelo",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    proveedor = table.Column<string>(type: "text", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    precio_entrada_mtok = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    precio_salida_mtok = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    precio_cache_lectura_mtok = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    precio_cache_escritura_mtok = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false, defaultValue: 0m),
                    moneda = table.Column<string>(type: "text", nullable: false, defaultValue: "USD")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ia_tarifa_modelo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ia_consumo_detalle",
                schema: "diagnostico",
                columns: table => new
                {
                    consumo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prompt_texto = table.Column<string>(type: "text", nullable: true),
                    respuesta_texto = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ia_consumo_detalle", x => x.consumo_id);
                    table.ForeignKey(
                        name: "fk_ia_consumo_detalle_log",
                        column: x => x.consumo_id,
                        principalSchema: "diagnostico",
                        principalTable: "ia_consumo_log",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_detalle_fecha",
                schema: "diagnostico",
                table: "ia_consumo_detalle",
                column: "fecha_inicio");

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_log_correlacion",
                schema: "diagnostico",
                table: "ia_consumo_log",
                column: "correlacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_log_fecha",
                schema: "diagnostico",
                table: "ia_consumo_log",
                column: "fecha_inicio");

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_log_modulo_fecha",
                schema: "diagnostico",
                table: "ia_consumo_log",
                columns: new[] { "modulo", "fecha_inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_log_placa",
                schema: "diagnostico",
                table: "ia_consumo_log",
                column: "placa",
                filter: "placa IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ia_consumo_log_usuario",
                schema: "diagnostico",
                table: "ia_consumo_log",
                columns: new[] { "usuario_id", "fecha_inicio" },
                filter: "usuario_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_ia_tarifa_modelo_proveedor_modelo_vigente",
                schema: "diagnostico",
                table: "ia_tarifa_modelo",
                columns: new[] { "proveedor", "modelo", "vigente_desde" },
                unique: true);

            // Contrato ia-consumo v1 (ADR-0090): la misma semilla de tarifas de Anthropic que core-api (DDL 202).
            migrationBuilder.Sql(Flit.Platform.Sdk.IaConsumo.IaTarifasSemilla.Sql(Flit.Diagnostico.Api.Persistence.DiagnosticoDb.Schema));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ia_consumo_detalle",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "ia_tarifa_modelo",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "ia_consumo_log",
                schema: "diagnostico");
        }
    }
}
