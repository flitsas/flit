using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P4_DerivacionTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tramite_creado_en",
                schema: "diagnostico",
                table: "vehiculos",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Instante en que Diagnóstico registró el borrador.");

            migrationBuilder.AddColumn<Guid>(
                name: "tramite_id",
                schema: "diagnostico",
                table: "vehiculos",
                type: "uuid",
                nullable: true,
                comment: "Borrador creado en Trámites (origin=diagnostico, external_ref=id). Se fija una vez (R-16).");

            migrationBuilder.AddColumn<string>(
                name: "tramite_numero",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Número (reference_number) del borrador en Trámites.");

            migrationBuilder.CreateTable(
                name: "derivacion_operaciones",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote y de la operación."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lote del que salen los vehículos."),
                    tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "INDIVIDUAL (síncrona, una placa) o MASIVA (encolada)."),
                    tipo_tramite_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tipo de trámite de los borradores (tramites.procedure_types.code)."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Usuario que lanzó la operación (sub); autor de los borradores (4.6). Seudónimo uuid."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "EN_CURSO o TERMINADA (creadas + omitidas + fallidas = total)."),
                    total = table.Column<int>(type: "integer", nullable: false, comment: "Ítems de la operación."),
                    creadas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Ítems CREADO."),
                    omitidas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Ítems OMITIDO."),
                    fallidas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0, comment: "Ítems FALLIDO."),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante de creación de la operación."),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante en que cerró su último ítem; null mientras está EN_CURSO."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derivacion_operaciones", x => x.id);
                    table.UniqueConstraint("uq_derivacion_operaciones_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_derivacion_operaciones_contadores", "creadas >= 0 AND omitidas >= 0 AND fallidas >= 0 AND creadas + omitidas + fallidas <= total");
                    table.CheckConstraint("ck_derivacion_operaciones_estado", "estado IN ('EN_CURSO', 'TERMINADA')");
                    table.CheckConstraint("ck_derivacion_operaciones_terminada", "(estado = 'TERMINADA') = (terminada_en IS NOT NULL)");
                    table.CheckConstraint("ck_derivacion_operaciones_tipo", "tipo IN ('INDIVIDUAL', 'MASIVA')");
                    table.CheckConstraint("ck_derivacion_operaciones_total", "total > 0");
                    table.ForeignKey(
                        name: "fk_derivacion_operaciones_diagnostico",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Operación «Crear trámite» (Punto 4, ADR-0084): individual o masiva, de un lote y un usuario, con contadores de sus ítems.");

            migrationBuilder.CreateTable(
                name: "derivaciones",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña de la operación y del vehículo."),
                    operacion_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operación a la que pertenece."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo a derivar (external_ref del borrador en Trámites)."),
                    orden = table.Column<int>(type: "integer", nullable: false, comment: "Orden de liberación dentro de la operación (el de la selección)."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "PENDIENTE, ENVIADO, CREADO, OMITIDO o FALLIDO."),
                    motivo_codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true, comment: "Motivo de OMITIDO o FALLIDO: NO_VIABLE, YA_TIENE_TRAMITE, OT_NO_HABILITADO, TRAMITES_NO_DISPONIBLE…; null en los demás."),
                    intentos = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0, comment: "Llamadas al rpc hechas por este ítem."),
                    disponible_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Espera del reintento programado (masivo); null si no espera."),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante en que salió hacia Trámites (en vuelo)."),
                    tramite_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Borrador creado en Trámites; solo en CREADO."),
                    tramite_numero = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Número (reference_number) del borrador."),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Último cambio de estado."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derivaciones", x => x.id);
                    table.UniqueConstraint("uq_derivaciones_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_derivaciones_creado", "(estado = 'CREADO') = (tramite_id IS NOT NULL)");
                    table.CheckConstraint("ck_derivaciones_estado", "estado IN ('PENDIENTE', 'ENVIADO', 'CREADO', 'OMITIDO', 'FALLIDO')");
                    table.CheckConstraint("ck_derivaciones_intentos", "intentos >= 0");
                    table.CheckConstraint("ck_derivaciones_motivo", "estado NOT IN ('OMITIDO', 'FALLIDO') OR motivo_codigo IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_derivaciones_operacion",
                        columns: x => new { x.tenant_id, x.operacion_id },
                        principalSchema: "diagnostico",
                        principalTable: "derivacion_operaciones",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_derivaciones_vehiculo",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Ítem de una operación «Crear trámite» (Punto 4, ADR-0084): un vehículo, su estado, motivo e intentos y el trámite creado.");

            migrationBuilder.CreateIndex(
                name: "ix_derivacion_operaciones_lote",
                schema: "diagnostico",
                table: "derivacion_operaciones",
                columns: new[] { "tenant_id", "diagnostico_id", "creada_en" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_derivaciones_en_vuelo",
                schema: "diagnostico",
                table: "derivaciones",
                columns: new[] { "tenant_id", "enviado_en" },
                filter: "estado = 'ENVIADO'");

            migrationBuilder.CreateIndex(
                name: "ix_derivaciones_operacion",
                schema: "diagnostico",
                table: "derivaciones",
                columns: new[] { "tenant_id", "operacion_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_derivaciones_pendientes",
                schema: "diagnostico",
                table: "derivaciones",
                columns: new[] { "tenant_id", "operacion_id", "orden" },
                filter: "estado = 'PENDIENTE'");

            migrationBuilder.CreateIndex(
                name: "ux_derivaciones_vehiculo_vivo",
                schema: "diagnostico",
                table: "derivaciones",
                columns: new[] { "tenant_id", "vehiculo_id" },
                unique: true,
                filter: "estado IN ('PENDIENTE', 'ENVIADO', 'CREADO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "derivaciones",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "derivacion_operaciones",
                schema: "diagnostico");

            migrationBuilder.DropColumn(
                name: "tramite_creado_en",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "tramite_id",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "tramite_numero",
                schema: "diagnostico",
                table: "vehiculos");
        }
    }
}
