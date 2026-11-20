using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModeloDelPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "public_code_seq",
                schema: "personanatural");

            migrationBuilder.CreateTable(
                name: "pii_access_log",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:medium Huella HMAC de la IP (no es la IP).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pii_access_log", x => x.id);
                },
                comment: "Auditoría de acceso a datos personales. Solo inserción, sin llaves foráneas ni disparadores (como el registro de acceso externo, ADR-0067).");

            migrationBuilder.CreateTable(
                name: "procedure_request",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    procedure_type_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    definition_version = table.Column<int>(type: "integer", nullable: false),
                    definition_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    transit_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transit_office_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    plate = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    vin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requester_document_hash = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:medium Huella HMAC del documento del solicitante (no es el documento); sirve para reanudar el borrador."),
                    field_values = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb", comment: "@pii:high Valores del formulario; pueden traer datos personales y se vacían al anonimizar el borrador."),
                    data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb", comment: "@pii:high Datos complementarios del trámite; pueden traer datos personales y se vacían al anonimizar el borrador."),
                    procedure_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    procedure_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    rectification_active = table.Column<bool>(type: "boolean", nullable: false),
                    rectification_reason = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procedure_request", x => x.id);
                    table.CheckConstraint("ck_procedure_request_status", "status IN ('borrador', 'pendiente_validacion', 'en_cola', 'creada', 'rechazada_creacion', 'cancelada')");
                    table.CheckConstraint("ck_procedure_request_vehicle", "plate IS NOT NULL OR vin IS NOT NULL");
                },
                comment: "Solicitud de un ciudadano para radicar un trámite en FLIT 2 (portal persona natural).");

            migrationBuilder.CreateTable(
                name: "query_quota",
                schema: "personanatural",
                columns: table => new
                {
                    quota_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "@pii:medium Clave del contador; puede incluir la huella HMAC del documento o de la IP (no el dato)."),
                    quota_date = table.Column<DateOnly>(type: "date", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_query_quota", x => new { x.quota_key, x.quota_date });
                },
                comment: "Contador diario para los límites por documento, IP y global; persistente para sobrevivir a los reinicios.");

            migrationBuilder.CreateTable(
                name: "resume_attempt",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false, comment: "SHA-256 del radicado normalizado, exista o no (no es un dato personal)."),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resume_attempt", x => x.id);
                },
                comment: "Intento fallido de reanudar con radicado y documento; sirve para bloquear el radicado tras varios fallos seguidos.");

            migrationBuilder.CreateTable(
                name: "retention_config",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    draft_retention_days = table.Column<int>(type: "integer", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    batch_size = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_config", x => x.id);
                    table.CheckConstraint("ck_retention_config", "id = 1 AND draft_retention_days >= 1 AND interval_minutes >= 1 AND batch_size >= 1");
                },
                comment: "Fila única con la política de retención de borradores; la cadencia vive en la base y se relee en cada ciclo.");

            migrationBuilder.CreateTable(
                name: "attachment",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "@pii:medium Nombre del archivo que subió el ciudadano; puede contener su nombre."),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachment", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachment_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Archivo adjunto de una solicitud; solo se guarda la referencia opaca al almacenamiento.");

            migrationBuilder.CreateTable(
                name: "consent",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_hash = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:medium Huella HMAC del documento de quien acepta (no es el documento)."),
                    text_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:medium Huella HMAC de la IP (no es la IP).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.SetNull);
                },
                comment: "Constancia del consentimiento de tratamiento de datos (Ley 1581): texto aceptado, momento y huella de quien acepta.");

            migrationBuilder.CreateTable(
                name: "identity_audit",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordinal = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_audit", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_audit_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Auditoría de los cambios de estado de la validación de identidad de cada parte. Sin datos personales; solo inserción.");

            migrationBuilder.CreateTable(
                name: "procedure_session",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:medium Hash del token de sesión (el token nunca se guarda)."),
                    ip_hash = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:medium Huella HMAC de la IP (no es la IP)."),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procedure_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_procedure_session_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Sesión efímera por trámite (el portal no tiene cuentas). Solo se guarda el hash del token.");

            migrationBuilder.CreateTable(
                name: "requester_person",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordinal = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    document_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    document_encrypted = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:high Número de documento cifrado (AES-GCM)."),
                    document_hash = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@pii:medium Huella HMAC del documento (no es el documento); sirve para búsquedas por igualdad."),
                    name_encrypted = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Nombre completo cifrado (AES-GCM)."),
                    email_encrypted = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:medium Correo electrónico cifrado (AES-GCM)."),
                    phone_encrypted = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:medium Celular cifrado (AES-GCM)."),
                    address_encrypted = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Dirección cifrada (AES-GCM)."),
                    identity_validation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    identity_validation_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "sin_solicitar"),
                    identity_validation_valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resend_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    last_resend_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requester_person", x => x.id);
                    table.ForeignKey(
                        name: "fk_requester_person_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Persona de una solicitud (vendedor, comprador, titular…). Datos personales cifrados en la aplicación (AES-GCM).");

            migrationBuilder.CreateTable(
                name: "status_history",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    procedure_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_status_history_procedure_request",
                        column: x => x.procedure_request_id,
                        principalSchema: "personanatural",
                        principalTable: "procedure_request",
                        principalColumn: "id",
                        onUpdate: ReferentialAction.Restrict,
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Historial de cambios de estado de una solicitud, tanto del portal como los que llegan de FLIT 2.");

            migrationBuilder.InsertData(
                schema: "personanatural",
                table: "retention_config",
                columns: new[] { "id", "draft_retention_days", "enabled", "interval_minutes", "batch_size", "updated_at" },
                values: new object[] { (short)1, 30, true, 1440, 200, new DateTimeOffset(new DateTime(2026, 10, 27, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_attachment_procedure_request",
                schema: "personanatural",
                table: "attachment",
                column: "procedure_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_procedure_request",
                schema: "personanatural",
                table: "consent",
                column: "procedure_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_audit_request_occurred",
                schema: "personanatural",
                table: "identity_audit",
                columns: new[] { "procedure_request_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pii_access_log_request_occurred",
                schema: "personanatural",
                table: "pii_access_log",
                columns: new[] { "procedure_request_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_procedure_request_procedure_instance",
                schema: "personanatural",
                table: "procedure_request",
                column: "procedure_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_procedure_request_requester_document_hash",
                schema: "personanatural",
                table: "procedure_request",
                column: "requester_document_hash");

            migrationBuilder.CreateIndex(
                name: "ix_procedure_request_type",
                schema: "personanatural",
                table: "procedure_request",
                columns: new[] { "tenant_id", "procedure_type_code", "definition_version" });

            migrationBuilder.CreateIndex(
                name: "uq_procedure_request_idempotency",
                schema: "personanatural",
                table: "procedure_request",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_procedure_request_public_code",
                schema: "personanatural",
                table: "procedure_request",
                column: "public_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_procedure_session_procedure_request",
                schema: "personanatural",
                table: "procedure_session",
                column: "procedure_request_id");

            migrationBuilder.CreateIndex(
                name: "uq_procedure_session_token_hash",
                schema: "personanatural",
                table: "procedure_session",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requester_person_document_hash",
                schema: "personanatural",
                table: "requester_person",
                column: "document_hash");

            migrationBuilder.CreateIndex(
                name: "uq_requester_person_request_role_ordinal",
                schema: "personanatural",
                table: "requester_person",
                columns: new[] { "procedure_request_id", "party_role", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resume_attempt_code_hash_occurred",
                schema: "personanatural",
                table: "resume_attempt",
                columns: new[] { "code_hash", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_status_history_request_occurred",
                schema: "personanatural",
                table: "status_history",
                columns: new[] { "procedure_request_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachment",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "consent",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "identity_audit",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "pii_access_log",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "procedure_session",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "query_quota",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "requester_person",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "resume_attempt",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "retention_config",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "status_history",
                schema: "personanatural");

            migrationBuilder.DropTable(
                name: "procedure_request",
                schema: "personanatural");

            migrationBuilder.DropSequence(
                name: "public_code_seq",
                schema: "personanatural");
        }
    }
}
