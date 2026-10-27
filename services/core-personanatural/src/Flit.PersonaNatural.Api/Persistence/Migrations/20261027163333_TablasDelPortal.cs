using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TablasDelPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acceso_pii_log",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ocurrio_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    accion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceso_pii_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuota_consulta",
                schema: "personanatural",
                columns: table => new
                {
                    clave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    dia = table.Column<DateOnly>(type: "date", nullable: false),
                    usadas = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuota_consulta", x => new { x.clave, x.dia });
                });

            migrationBuilder.CreateTable(
                name: "solicitud_tramite",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_publico = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo_tramite_codigo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    definicion_version = table.Column<int>(type: "integer", nullable: false),
                    definicion_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    organismo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organismo_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    vin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cedula_solicitante_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    field_values = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    datos = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    procedure_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    estado_tramite = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    subsanacion_activa = table.Column<bool>(type: "boolean", nullable: false),
                    subsanacion_motivo = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expira_borrador_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitud_tramite", x => x.id);
                    table.CheckConstraint("ck_solicitud_estado", "estado IN ('borrador', 'pendiente_validacion', 'en_cola', 'creada', 'rechazada_creacion', 'cancelada')");
                    table.CheckConstraint("ck_solicitud_vehiculo", "placa IS NOT NULL OR vin IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "adjunto",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjunto", x => x.id);
                    table.ForeignKey(
                        name: "fk_adjunto_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consentimiento",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: true),
                    documento_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    texto_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    aceptado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consentimiento", x => x.id);
                    table.ForeignKey(
                        name: "fk_consentimiento_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "historial_estado",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    hasta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocurrio_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historial_estado", x => x.id);
                    table.ForeignKey(
                        name: "fk_historial_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "persona_solicitante",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parte = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordinal = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    tipo_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    documento_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    documento_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    nombre_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    correo_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    celular_cifrado = table.Column<byte[]>(type: "bytea", nullable: true),
                    direccion_cifrada = table.Column<byte[]>(type: "bytea", nullable: true),
                    validacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    validacion_estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "sin_solicitar"),
                    validacion_vigente_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persona_solicitante", x => x.id);
                    table.ForeignKey(
                        name: "fk_persona_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sesion_tramite",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocada = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sesion_tramite", x => x.id);
                    table.ForeignKey(
                        name: "fk_sesion_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acceso_pii_solicitud_fecha",
                schema: "personanatural",
                table: "acceso_pii_log",
                columns: new[] { "solicitud_id", "ocurrio_en" });

            migrationBuilder.CreateIndex(
                name: "ix_adjunto_solicitud",
                schema: "personanatural",
                table: "adjunto",
                column: "solicitud_id");

            migrationBuilder.CreateIndex(
                name: "ix_consentimiento_solicitud",
                schema: "personanatural",
                table: "consentimiento",
                column: "solicitud_id");

            migrationBuilder.CreateIndex(
                name: "ix_historial_solicitud_fecha",
                schema: "personanatural",
                table: "historial_estado",
                columns: new[] { "solicitud_id", "ocurrio_en" });

            migrationBuilder.CreateIndex(
                name: "ix_persona_documento_hash",
                schema: "personanatural",
                table: "persona_solicitante",
                column: "documento_hash");

            migrationBuilder.CreateIndex(
                name: "uq_persona_solicitud_parte_ordinal",
                schema: "personanatural",
                table: "persona_solicitante",
                columns: new[] { "solicitud_id", "parte", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesion_solicitud",
                schema: "personanatural",
                table: "sesion_tramite",
                column: "solicitud_id");

            migrationBuilder.CreateIndex(
                name: "uq_sesion_token_hash",
                schema: "personanatural",
                table: "sesion_tramite",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_cedula_hash",
                schema: "personanatural",
                table: "solicitud_tramite",
                column: "cedula_solicitante_hash");

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_procedure_instance",
                schema: "personanatural",
                table: "solicitud_tramite",
                column: "procedure_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_tipo",
                schema: "personanatural",
                table: "solicitud_tramite",
                columns: new[] { "tipo_tramite_codigo", "definicion_version" });

            migrationBuilder.CreateIndex(
                name: "uq_solicitud_codigo_publico",
                schema: "personanatural",
                table: "solicitud_tramite",
                column: "codigo_publico",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_solicitud_idem",
                schema: "personanatural",
                table: "solicitud_tramite",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceso_pii_log",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "adjunto",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "consentimiento",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "cuota_consulta",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "historial_estado",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "persona_solicitante",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "sesion_tramite",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "solicitud_tramite",
                schema: "personanatural");
        }
    }
}
