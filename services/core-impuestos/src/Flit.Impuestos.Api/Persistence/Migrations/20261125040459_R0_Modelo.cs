using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.Impuestos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class R0_Modelo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aprovisionamientos",
                schema: "impuestos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    motivo_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    motivo_detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Detalle técnico del motivo, sin secretos ni PII."),
                    estado_previo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    firma_confirmada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    modo_respaldo_firma = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    firmas_invalidas_consecutivas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    intentos_automaticos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_cambio_plataforma_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "eventId del último trabajo impuestos.producto.cambiado aplicado."),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    reportado_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aprovisionamientos", x => x.tenant_id);
                    table.CheckConstraint("ck_aprovisionamientos_error_tipo", "error_tipo IS NULL OR error_tipo IN ('DATOS', 'AMBIENTE')");
                    table.CheckConstraint("ck_aprovisionamientos_estado", "estado IN ('PENDIENTE', 'LISTO', 'LISTO_SIN_WEBHOOK', 'ERROR', 'INCIERTO', 'REQUIERE_CREDENCIALES', 'REQUIERE_REVISION_NIT', 'SUSPENDIDO')");
                    table.CheckConstraint("ck_aprovisionamientos_estado_previo", "estado_previo IS NULL OR estado_previo IN ('PENDIENTE', 'LISTO', 'LISTO_SIN_WEBHOOK', 'ERROR', 'INCIERTO', 'REQUIERE_CREDENCIALES', 'REQUIERE_REVISION_NIT', 'SUSPENDIDO')");
                },
                comment: "Estado del aprovisionamiento de cada compañía en el API de liquidación (plan §3.1). Sin secretos ni PII.");

            migrationBuilder.CreateTable(
                name: "auditoria_lectura_backoffice",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurso = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria_lectura_backoffice", x => x.id);
                },
                comment: "Lecturas del Super Admin entre compañías (forma ADR-0073). Solo inserción.");

            migrationBuilder.CreateTable(
                name: "avisos_recibidos",
                schema: "impuestos",
                columns: table => new
                {
                    delivery_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consulta_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: true),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resultado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos_recibidos", x => x.delivery_id);
                },
                comment: "Dedupe de avisos por X-Flit-Delivery; sin cuerpo (PII). Purga a los 30 días. Global.");

            migrationBuilder.CreateTable(
                name: "configuracion_empresa",
                schema: "impuestos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_registros_lote_contractual = table.Column<int>(type: "integer", nullable: false, defaultValue: 500),
                    departamentos_habilitados = table.Column<string[]>(type: "varchar(2)[]", nullable: true, comment: "NULL = todos los activos de /fuentes."),
                    max_registros_lote_operativo = table.Column<int>(type: "integer", nullable: true),
                    correos_aviso = table.Column<string[]>(type: "varchar(254)[]", nullable: false, defaultValueSql: "'{}'", comment: "@pii:low Correos de aviso de la compañía."),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_empresa", x => x.tenant_id);
                    table.CheckConstraint("ck_configuracion_empresa_max_contractual", "max_registros_lote_contractual BETWEEN 1 AND 5000");
                },
                comment: "Configuración de Impuestos por compañía (contractual y operativa).");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                },
                comment: "Llaves de ASP.NET Data Protection del servicio, cifradas con su certificado. Sin tenant.");

            migrationBuilder.CreateTable(
                name: "fuentes_api",
                schema: "impuestos",
                columns: table => new
                {
                    departamento_codigo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    params_schema = table.Column<string>(type: "jsonb", nullable: false),
                    sincronizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuentes_api", x => x.departamento_codigo);
                },
                comment: "Caché de GET /fuentes del API de liquidación (global, refresco cada 6 h).");

            migrationBuilder.CreateTable(
                name: "historial_aprovisionamiento",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    hacia = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    motivo_codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historial_aprovisionamiento", x => x.id);
                    table.CheckConstraint("ck_historial_aprovisionamiento_desde", "desde IS NULL OR desde IN ('PENDIENTE', 'LISTO', 'LISTO_SIN_WEBHOOK', 'ERROR', 'INCIERTO', 'REQUIERE_CREDENCIALES', 'REQUIERE_REVISION_NIT', 'SUSPENDIDO')");
                    table.CheckConstraint("ck_historial_aprovisionamiento_hacia", "hacia IN ('PENDIENTE', 'LISTO', 'LISTO_SIN_WEBHOOK', 'ERROR', 'INCIERTO', 'REQUIERE_CREDENCIALES', 'REQUIERE_REVISION_NIT', 'SUSPENDIDO')");
                },
                comment: "Transiciones del aprovisionamiento por compañía (plan §3.1). Solo inserción.");

            migrationBuilder.CreateTable(
                name: "liquidaciones",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    estado = table.Column<string>(type: "character varying(28)", maxLength: 28, nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    c_exitosas = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_con_deuda = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_paz_y_salvo = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_error = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_datos_incompletos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_dpto_no_habilitado = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_incierto = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_sin_resultado = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    c_pendientes = table.Column<int>(type: "integer", nullable: false),
                    config_copia = table.Column<string>(type: "jsonb", nullable: false, comment: "{maxRegistros, departamentosHabilitados, concurrencia} al crear (F3-CF3)."),
                    creada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aviso_enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidaciones", x => x.id);
                    table.CheckConstraint("ck_liquidaciones_estado", "estado IN ('CREADO', 'EN_PROCESO', 'COMPLETADO', 'COMPLETADO_CON_NOVEDADES', 'CERRADO_MANUALMENTE')");
                    table.CheckConstraint("ck_liquidaciones_tipo", "tipo IN ('INDIVIDUAL', 'MASIVA')");
                },
                comment: "Liquidaciones (lotes) de la compañía; la individual es un lote de un ítem (S-A).");

            migrationBuilder.CreateTable(
                name: "notificaciones",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    leida_por = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notificaciones", x => x.id);
                },
                comment: "Avisos en pantalla de la compañía (DA-6b). Sin PII.");

            migrationBuilder.CreateTable(
                name: "organismos",
                schema: "impuestos",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    nombre_normalizado = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    departamento_codigo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    departamento = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    municipio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    sincronizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organismos", x => x.codigo);
                },
                comment: "Réplica diaria de los organismos de tránsito (catálogo global, sin empresa).");

            migrationBuilder.CreateTable(
                name: "credenciales_empresa",
                schema: "impuestos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_cifrada = table.Column<byte[]>(type: "bytea", nullable: false, comment: "@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.api_key."),
                    api_key_prefijo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true, comment: "Prefijo de la api_key para soporte; no es secreto."),
                    webhook_secret_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@secret Data Protection, purpose impuestos.credenciales.v1.{tenant}.webhook_secret."),
                    empresa_api_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    origen = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credenciales_empresa", x => x.tenant_id);
                    table.CheckConstraint("ck_credenciales_empresa_origen", "origen IN ('ALTA', 'COPIA_DIAGNOSTICO', 'CARGA_MANUAL')");
                    table.ForeignKey(
                        name: "fk_credenciales_empresa_aprovisionamientos",
                        column: x => x.tenant_id,
                        principalSchema: "impuestos",
                        principalTable: "aprovisionamientos",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Credenciales del API de liquidación por compañía, cifradas con Data Protection (DA-2=b).");

            migrationBuilder.CreateTable(
                name: "liquidacion_items",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    liquidacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fila = table.Column<int>(type: "integer", nullable: true),
                    placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, comment: "Placa normalizada (S16)."),
                    estado = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_codigo = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    error_clase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    error_mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    campo_faltante = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    departamento_codigo = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    departamento_nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    secretaria_ciudad = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    documento_tipo = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    documento_numero_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Número de documento del propietario, cifrado (impuestos.pii.v1)."),
                    propietario_nombre_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Nombre del propietario, cifrado (impuestos.pii.v1)."),
                    consulta_api_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: true, comment: "SHA-256 del token del callbackUrl; el token no se guarda."),
                    con_callback = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    intentos_runt = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    intentos_reenvio = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proximo_poll_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_api_max = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    consulta_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    resultado_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    parcial = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    total_a_pagar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true, comment: "NULL no es 0 (F2-CF14)."),
                    resultado_cifrado = table.Column<byte[]>(type: "bytea", nullable: true, comment: "@pii:high Cuerpo completo del resultado, cifrado (impuestos.resultado.v1)."),
                    warnings = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    resultado_tardio = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    relanzado_desde = table.Column<Guid>(type: "uuid", nullable: true),
                    posible_duplicado = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidacion_items", x => x.id);
                    table.CheckConstraint("ck_liquidacion_items_error_clase", "error_clase IS NULL OR error_clase IN ('CORREGIR_DATOS', 'DEPENDE', 'REINTENTABLE')");
                    table.CheckConstraint("ck_liquidacion_items_estado", "estado IN ('PENDIENTE', 'CONSULTANDO_RUNT', 'REINTENTO_PROGRAMADO', 'ENVIANDO', 'ENVIADO', 'INCIERTO', 'INCIERTO_REQUIERE_DECISION', 'DATOS_INCOMPLETOS', 'DEPARTAMENTO_NO_HABILITADO', 'EXITOSO', 'ERROR', 'SIN_RESULTADO_LOTE_CERRADO')");
                    table.ForeignKey(
                        name: "fk_liquidacion_items_liquidaciones",
                        column: x => x.liquidacion_id,
                        principalSchema: "impuestos",
                        principalTable: "liquidaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_liquidacion_items_relanzado_desde",
                        column: x => x.relanzado_desde,
                        principalSchema: "impuestos",
                        principalTable: "liquidacion_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Placas de cada liquidación con su estado y resultado. PII cifrada (DA-7).");

            migrationBuilder.CreateTable(
                name: "liquidacion_periodos",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    avaluo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    impuesto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    sancion = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    intereses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    descuentos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pagos_previos = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    total_a_pagar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    otros_cargos = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'"),
                    vencimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    referencia_pago = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    file_id_api = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    no_liquidable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_liquidacion_periodos", x => x.id);
                    table.ForeignKey(
                        name: "fk_liquidacion_periodos_liquidacion_items",
                        column: x => x.item_id,
                        principalSchema: "impuestos",
                        principalTable: "liquidacion_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Vigencias liquidadas de cada ítem. Sin PII.");

            migrationBuilder.CreateTable(
                name: "recibos",
                schema: "impuestos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id_api = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tipo = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    periodos = table.Column<string[]>(type: "varchar(9)[]", nullable: false, defaultValueSql: "'{}'"),
                    estado = table.Column<string>(type: "character varying(28)", maxLength: 28, nullable: false),
                    referencia_almacen = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sha256 = table.Column<string>(type: "char(64)", nullable: true),
                    tamano = table.Column<long>(type: "bigint", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    motivo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recibos", x => x.id);
                    table.CheckConstraint("ck_recibos_estado", "estado IN ('PENDIENTE', 'GUARDADO', 'SIN_ARCHIVO', 'PENDIENTE_REINTENTO_MANUAL')");
                    table.CheckConstraint("ck_recibos_tipo", "tipo IN ('VIGENCIA', 'CONSOLIDADO', 'PAZ_Y_SALVO')");
                    table.ForeignKey(
                        name: "fk_recibos_liquidacion_items",
                        column: x => x.item_id,
                        principalSchema: "impuestos",
                        principalTable: "liquidacion_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Recibos PDF de cada ítem (un archivo por fileId, DA-9c).");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_lectura_backoffice_tenant_en",
                schema: "impuestos",
                table: "auditoria_lectura_backoffice",
                columns: new[] { "tenant_id", "en" });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_recibidos_recibido_en",
                schema: "impuestos",
                table: "avisos_recibidos",
                column: "recibido_en");

            migrationBuilder.CreateIndex(
                name: "ix_historial_aprovisionamiento_tenant_en",
                schema: "impuestos",
                table: "historial_aprovisionamiento",
                columns: new[] { "tenant_id", "en" });

            migrationBuilder.CreateIndex(
                name: "ix_items_liq",
                schema: "impuestos",
                table: "liquidacion_items",
                columns: new[] { "liquidacion_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_items_poll",
                schema: "impuestos",
                table: "liquidacion_items",
                column: "proximo_poll_en",
                filter: "estado IN ('ENVIADO','INCIERTO')");

            migrationBuilder.CreateIndex(
                name: "ix_items_tenant_placa",
                schema: "impuestos",
                table: "liquidacion_items",
                columns: new[] { "tenant_id", "placa" });

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_items_relanzado_desde",
                schema: "impuestos",
                table: "liquidacion_items",
                column: "relanzado_desde");

            migrationBuilder.CreateIndex(
                name: "ux_liquidacion_items_token_hash",
                schema: "impuestos",
                table: "liquidacion_items",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_liquidacion_periodos_item",
                schema: "impuestos",
                table: "liquidacion_periodos",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_liq_tenant_creada",
                schema: "impuestos",
                table: "liquidaciones",
                columns: new[] { "tenant_id", "creada_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notificaciones_tenant_creada",
                schema: "impuestos",
                table: "notificaciones",
                columns: new[] { "tenant_id", "creada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_org_nombre",
                schema: "impuestos",
                table: "organismos",
                column: "nombre_normalizado");

            migrationBuilder.CreateIndex(
                name: "ux_recibos_item_file",
                schema: "impuestos",
                table: "recibos",
                columns: new[] { "item_id", "file_id_api" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_lectura_backoffice",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "avisos_recibidos",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "configuracion_empresa",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "credenciales_empresa",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "fuentes_api",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "historial_aprovisionamiento",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "liquidacion_periodos",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "notificaciones",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "organismos",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "recibos",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "aprovisionamientos",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "liquidacion_items",
                schema: "impuestos");

            migrationBuilder.DropTable(
                name: "liquidaciones",
                schema: "impuestos");
        }
    }
}
