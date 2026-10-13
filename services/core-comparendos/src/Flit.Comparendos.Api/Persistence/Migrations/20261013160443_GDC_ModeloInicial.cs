using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GDC_ModeloInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "comparendos");

            migrationBuilder.CreateTable(
                name: "alerta_configuracion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    alertas_habilitadas = table.Column<bool>(type: "boolean", nullable: false),
                    umbral_fallos_ciclo = table.Column<int>(type: "integer", nullable: false),
                    plantilla_asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    remitente_nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    correos_internos_reportes = table.Column<List<string>>(type: "text[]", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_configuracion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alerta_destinatario",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    correo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tipo_alerta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_destinatario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archivo",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dominio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre_original = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archivo_local",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contenido = table.Column<byte[]>(type: "bytea", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archivo_local", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auditoria_configuracion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entidad = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entidad_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    accion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cambios = table.Column<string>(type: "jsonb", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria_configuracion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aviso_conductor",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_aviso = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    disparo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ventana_al_envio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    porcentaje_descuento = table.Column<short>(type: "smallint", nullable: false),
                    fecha_limite_ventana = table.Column<DateOnly>(type: "date", nullable: true),
                    correo_destino = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    comunicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portal_enlace_id = table.Column<Guid>(type: "uuid", nullable: true),
                    enviado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aviso_conductor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "calendario_dia",
                schema: "comparendos",
                columns: table => new
                {
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    es_habil = table.Column<bool>(type: "boolean", nullable: false),
                    es_fin_de_semana = table.Column<bool>(type: "boolean", nullable: false),
                    es_festivo = table.Column<bool>(type: "boolean", nullable: false),
                    descripcion_festivo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendario_dia", x => x.fecha);
                });

            migrationBuilder.CreateTable(
                name: "cambio_contraventor_solicitud",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_intento = table.Column<short>(type: "smallint", nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representante_legal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contraventor_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    formato_archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comunicacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aviso4comunicacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    causal_rechazo = table.Column<string>(type: "text", nullable: true),
                    fecha_respuesta_ot = table.Column<DateOnly>(type: "date", nullable: true),
                    soporte_respuesta_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    respondido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    respondido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cambio_contraventor_solicitud", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carga_masiva_pagos",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    total_filas = table.Column<int>(type: "integer", nullable: false),
                    filas_validas = table.Column<int>(type: "integer", nullable: false),
                    filas_invalidas = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cargado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    cargado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cargado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carga_masiva_pagos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "carga_masiva_pagos_fila",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    carga_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_fila = table.Column<int>(type: "integer", nullable: false),
                    datos = table.Column<string>(type: "jsonb", nullable: false),
                    valida = table.Column<bool>(type: "boolean", nullable: false),
                    errores = table.Column<List<string>>(type: "text[]", nullable: false),
                    pago_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carga_masiva_pagos_fila", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ciclo_ingesta",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    disparo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tenant_id_manual = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    inicio_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claves_total = table.Column<int>(type: "integer", nullable: false),
                    claves_error = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mensaje_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ciclo_ingesta", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comparendo",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_normalizado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo_ingreso = table.Column<short>(type: "smallint", nullable: false),
                    fuente_principal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    placa = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    identificacion_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    placa_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nit_consultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tipo_documento_infractor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    numero_documento_infractor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nombre_infractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    codigo_infraccion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    descripcion_infraccion = table.Column<string>(type: "text", nullable: true),
                    direccion_infraccion = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    fecha_imposicion = table.Column<DateOnly>(type: "date", nullable: true),
                    hora_imposicion = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    fecha_notificacion = table.Column<DateOnly>(type: "date", nullable: true),
                    es_fotomulta = table.Column<bool>(type: "boolean", nullable: true),
                    secretaria_texto = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_vehiculo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    servicio_vehiculo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    valor_capital = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    valor_adicional = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    valor_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    estado_fuente = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    en_cobro_coactivo = table.Column<bool>(type: "boolean", nullable: false),
                    foto_evidencia_url = table.Column<string>(type: "text", nullable: true),
                    archivo_evidencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_limite_gestion = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_fin_descuento50 = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_fin_descuento25 = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_prescripcion = table.Column<DateOnly>(type: "date", nullable: true),
                    plazos_calculados_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_inactivacion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    detalle_inactivacion = table.Column<string>(type: "text", nullable: true),
                    inactivado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inactivado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_gestion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    estado_notificacion_conductor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    estado_cambio_contraventor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    estado_consulta_contraventor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    intentos_consulta_contraventor = table.Column<short>(type: "smallint", nullable: false),
                    notificado_fisicamente = table.Column<bool>(type: "boolean", nullable: false),
                    pagado = table.Column<bool>(type: "boolean", nullable: false),
                    tiene_dp = table.Column<bool>(type: "boolean", nullable: false),
                    gestionado = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_evento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    legacy_v1id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparendo", x => x.id);
                    table.CheckConstraint("ck_comparendo_inactivo_con_motivo", "activo OR motivo_inactivacion IS NOT NULL");
                    table.CheckConstraint("ck_comparendo_ocr_con_evidencia", "tipo_ingreso NOT IN (2, 3) OR archivo_evidencia_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "comunicacion_adjunto",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comunicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comunicacion_adjunto", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comunicacion_intento",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comunicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<short>(type: "smallint", nullable: false),
                    resultado_plataforma = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    mensaje = table.Column<string>(type: "text", nullable: true),
                    duracion_ms = table.Column<int>(type: "integer", nullable: false),
                    intentado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comunicacion_intento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comunicacion_saliente",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    canal_correo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    remitente = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    remitente_nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    destinatarios = table.Column<string>(type: "jsonb", nullable: false),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cuerpo_html_archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_clave = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    plantilla_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cantidad_reintentos = table.Column<short>(type: "smallint", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entrega_id_plataforma = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resultado_plataforma = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    motivo_fallo = table.Column<string>(type: "text", nullable: true),
                    desviado = table.Column<bool>(type: "boolean", nullable: false),
                    abierto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aperturas = table.Column<int>(type: "integer", nullable: false),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comunicacion_saliente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_consultas",
                schema: "comparendos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    activada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    activada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_consultas", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_empresa",
                schema: "comparendos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    has_right_petition_module = table.Column<bool>(type: "boolean", nullable: false),
                    allows_contravener_consultation = table.Column<bool>(type: "boolean", nullable: false),
                    allows_contravener_change = table.Column<bool>(type: "boolean", nullable: false),
                    has_ocr_access = table.Column<bool>(type: "boolean", nullable: false),
                    has_reportes_module = table.Column<bool>(type: "boolean", nullable: false),
                    notification_channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    canal_correo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    remitente_nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    correo_respuesta = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    cc = table.Column<string>(type: "jsonb", nullable: false),
                    bcc = table.Column<string>(type: "jsonb", nullable: false),
                    tipo_integracion_conductor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    conjunto_plantillas = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requiere_representante_legal = table.Column<bool>(type: "boolean", nullable: false),
                    destinatarios_reportes = table.Column<List<string>>(type: "text[]", nullable: false),
                    correos_alerta_operativa = table.Column<List<string>>(type: "text[]", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_empresa", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "contraventor_historial",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    datos_anteriores = table.Column<string>(type: "jsonb", nullable: true),
                    datos_nuevos = table.Column<string>(type: "jsonb", nullable: false),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contraventor_historial", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "derecho_peticion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipologia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    radicado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correos_destino = table.Column<List<string>>(type: "text[]", nullable: false),
                    representante_legal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    peticionario_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    estado_legal_al_radicar = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    valor_al_radicar = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    documento_archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firmado_digitalmente = table.Column<bool>(type: "boolean", nullable: false),
                    comunicacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    fecha_radicacion = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_limite_respuesta_ot = table.Column<DateOnly>(type: "date", nullable: true),
                    respuesta_sentido = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    respuesta_origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    respuesta_radicado_ot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    respuesta_fecha = table.Column<DateOnly>(type: "date", nullable: true),
                    respuesta_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    respuesta_registrada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    respuesta_registrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enviado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    legacy_v1id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derecho_peticion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_ocr_edicion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_ocr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    editado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_ocr_edicion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_ocr_intento",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_ocr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<short>(type: "smallint", nullable: false),
                    tipo_error = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mensaje_error = table.Column<string>(type: "text", nullable: true),
                    resultado = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    intentado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proximo_reintento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_ocr_intento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_soporte",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_soporte", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exportacion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    filtros = table.Column<string>(type: "jsonb", nullable: false),
                    total_filas = table.Column<int>(type: "integer", nullable: true),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exportacion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identificacion_monitoreada",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_base = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    digito_verificacion = table.Column<short>(type: "smallint", nullable: true),
                    nombre_titular = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identificacion_monitoreada", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox",
                schema: "comparendos",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox", x => new { x.event_id, x.consumer });
                });

            migrationBuilder.CreateTable(
                name: "infraccion_catalogo",
                schema: "comparendos",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_infraccion_catalogo", x => x.codigo);
                });

            migrationBuilder.CreateTable(
                name: "lote_dp",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipologia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    enviados = table.Column<int>(type: "integer", nullable: false),
                    omitidos = table.Column<int>(type: "integer", nullable: false),
                    fallidos = table.Column<int>(type: "integer", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lote_dp", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lote_dp_item",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    derecho_peticion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lote_dp_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "municipio_fuente",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_fuente = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_municipio_fuente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organismo_transito",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    codigo_divipola = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    departamento = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    municipio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    activo_plataforma = table.Column<bool>(type: "boolean", nullable: false),
                    correos_radicacion = table.Column<List<string>>(type: "text[]", nullable: false),
                    permite_cambio_contraventor = table.Column<bool>(type: "boolean", nullable: false),
                    sincronizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organismo_transito", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    routing_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pago",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_pago = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    valor_pagado = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: false),
                    comprobante_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    carga_masiva_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    legacy_v1id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pago", x => x.id);
                    table.CheckConstraint("ck_pago_valor_positivo", "valor_pagado > 0");
                });

            migrationBuilder.CreateTable(
                name: "paquete_ocr",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cantidad_documentos = table.Column<int>(type: "integer", nullable: false),
                    cantidad_pendientes = table.Column<int>(type: "integer", nullable: false),
                    cantidad_completos = table.Column<int>(type: "integer", nullable: false),
                    cantidad_incompletos = table.Column<int>(type: "integer", nullable: false),
                    cantidad_fallidos = table.Column<int>(type: "integer", nullable: false),
                    cantidad_duplicados = table.Column<int>(type: "integer", nullable: false),
                    cantidad_registrados = table.Column<int>(type: "integer", nullable: false),
                    cantidad_descartados = table.Column<int>(type: "integer", nullable: false),
                    prompt_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    procesamiento_iniciado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paquete_ocr", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "placa_monitoreada",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    placa = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    origen_registro = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_placa_monitoreada", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plantilla",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conjunto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipologia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    version_publicada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portal_enlace",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contraventor_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_revocacion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    accesos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_acceso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portal_enlace", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_ocr_version",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    modelo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    publicada = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_ocr_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "radicado_reservado",
                schema: "comparendos",
                columns: table => new
                {
                    radicado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reservado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_radicado_reservado", x => x.radicado);
                });

            migrationBuilder.CreateTable(
                name: "representante_legal",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alcance = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    identificacion_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombres = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cargo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    correo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    direccion_notificacion = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    firma_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cedula_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    certificado_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    escritura_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_expedicion_certificado = table.Column<DateOnly>(type: "date", nullable: true),
                    permiso_envio_dp = table.Column<bool>(type: "boolean", nullable: false),
                    permiso_cambio_contraventor = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_representante_legal", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduler_aviso",
                schema: "comparendos",
                columns: table => new
                {
                    tipo_aviso = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    hora_envio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduler_aviso", x => x.tipo_aviso);
                });

            migrationBuilder.CreateTable(
                name: "tipologia_dp",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion_juridica = table.Column<string>(type: "text", nullable: false),
                    prefijo_radicado = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    estados_legales_permitidos = table.Column<List<string>>(type: "text[]", nullable: false),
                    permite_masivo = table.Column<bool>(type: "boolean", nullable: false),
                    plazo_respuesta_ot_dias_habiles = table.Column<short>(type: "smallint", nullable: false),
                    adjuntos_requeridos = table.Column<List<string>>(type: "text[]", nullable: false),
                    alias_v2 = table.Column<List<string>>(type: "text[]", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    legacy_v1codigo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipologia_dp", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trabajo_programado",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expresion_cron = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    solo_dias_habiles = table.Column<bool>(type: "boolean", nullable: false),
                    ultima_ejecucion_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trabajo_programado", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consulta_fuente",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciclo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    clave_consultada = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    municipio_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo_error = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mensaje_error = table.Column<string>(type: "text", nullable: true),
                    cantidad_items = table.Column<int>(type: "integer", nullable: false),
                    respuesta_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duracion_ms = table.Column<int>(type: "integer", nullable: false),
                    consultado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consulta_fuente", x => x.id);
                    table.ForeignKey(
                        name: "fk_consulta_fuente_ciclo_ingesta_ciclo_id",
                        column: x => x.ciclo_id,
                        principalSchema: "comparendos",
                        principalTable: "ciclo_ingesta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "log_migracion",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ciclo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    identificacion_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    placa_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    clave = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    municipio_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    inicio_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fin_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cantidad_obtenidas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_nuevas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_actualizadas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_sin_cambios = table.Column<int>(type: "integer", nullable: false),
                    cantidad_duplicadas_otra_fuente = table.Column<int>(type: "integer", nullable: false),
                    cantidad_inactivadas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_reactivadas = table.Column<int>(type: "integer", nullable: false),
                    cantidad_dp_favorable_automatico = table.Column<int>(type: "integer", nullable: false),
                    resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mensaje_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_migracion", x => x.id);
                    table.ForeignKey(
                        name: "fk_log_migracion_ciclo_ingesta_ciclo_id",
                        column: x => x.ciclo_id,
                        principalSchema: "comparendos",
                        principalTable: "ciclo_ingesta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comparendo_cambio",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ciclo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor_anterior = table.Column<string>(type: "text", nullable: true),
                    valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    aplicado_a_maestro = table.Column<bool>(type: "boolean", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparendo_cambio", x => x.id);
                    table.ForeignKey(
                        name: "fk_comparendo_cambio_comparendo_comparendo_id",
                        column: x => x.comparendo_id,
                        principalSchema: "comparendos",
                        principalTable: "comparendo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comparendo_fuente",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    identificacion_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    placa_monitoreada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    municipio_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    presente = table.Column<bool>(type: "boolean", nullable: false),
                    datos_normalizados = table.Column<string>(type: "jsonb", nullable: false),
                    hash_datos = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    primera_vez_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_vez_vista_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ausente_desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_ciclo_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comparendo_fuente", x => x.id);
                    table.ForeignKey(
                        name: "fk_comparendo_fuente_comparendo_comparendo_id",
                        column: x => x.comparendo_id,
                        principalSchema: "comparendos",
                        principalTable: "comparendo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contraventor",
                schema: "comparendos",
                columns: table => new
                {
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombres = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefono = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    direccion = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sucursal = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tipo_contrato = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    origen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    declaracion_tratamiento_datos = table.Column<bool>(type: "boolean", nullable: false),
                    respuesta_api_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contraventor", x => x.comparendo_id);
                    table.ForeignKey(
                        name: "fk_contraventor_comparendo_comparendo_id",
                        column: x => x.comparendo_id,
                        principalSchema: "comparendos",
                        principalTable: "comparendo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "timeline_evento",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    actor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    datos = table.Column<string>(type: "jsonb", nullable: true),
                    relacionado_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    relacionado_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timeline_evento", x => x.id);
                    table.ForeignKey(
                        name: "fk_timeline_evento_comparendo_comparendo_id",
                        column: x => x.comparendo_id,
                        principalSchema: "comparendos",
                        principalTable: "comparendo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configuracion_consulta_canal",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    encendido = table.Column<bool>(type: "boolean", nullable: false),
                    proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    horas = table.Column<List<string>>(type: "text[]", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "integer", nullable: true),
                    ventana_desde = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ventana_hasta = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    solo_dias_habiles = table.Column<bool>(type: "boolean", nullable: false),
                    ultima_ejecucion_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracion_consulta_canal", x => x.id);
                    table.ForeignKey(
                        name: "fk_configuracion_consulta_canal_configuracion_consultas_tenant",
                        column: x => x.tenant_id,
                        principalSchema: "comparendos",
                        principalTable: "configuracion_consultas",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organismo_transito_alias",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto_normalizado = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organismo_transito_alias", x => x.id);
                    table.ForeignKey(
                        name: "fk_organismo_transito_alias_organismo_transito_organismo_trans",
                        column: x => x.organismo_transito_id,
                        principalSchema: "comparendos",
                        principalTable: "organismo_transito",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permiso_ot_empresa",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organismo_transito_id = table.Column<Guid>(type: "uuid", nullable: false),
                    representante_legal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permite_consulta_contraventor = table.Column<bool>(type: "boolean", nullable: false),
                    permite_cambio_contraventor = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permiso_ot_empresa", x => x.id);
                    table.ForeignKey(
                        name: "fk_permiso_ot_empresa_organismo_transito_organismo_transito_id",
                        column: x => x.organismo_transito_id,
                        principalSchema: "comparendos",
                        principalTable: "organismo_transito",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documento_ocr",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tipo_archivo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    intentos = table.Column<short>(type: "smallint", nullable: false),
                    proveedor_modelo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    respuesta_llm = table.Column<string>(type: "jsonb", nullable: true),
                    numero_comparendo_extraido = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    numero_normalizado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    campos_faltantes = table.Column<List<string>>(type: "text[]", nullable: false),
                    duplicado_de_comparendo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duplicado_de_documento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fuente_duplicado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    comparendo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_fallo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mensaje_error = table.Column<string>(type: "text", nullable: true),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revisado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    descartado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_descarte = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_ocr", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_ocr_paquete_ocr_paquete_id",
                        column: x => x.paquete_id,
                        principalSchema: "comparendos",
                        principalTable: "paquete_ocr",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plantilla_version",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_version = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cuerpo_html = table.Column<string>(type: "text", nullable: false),
                    variables_usadas = table.Column<List<string>>(type: "text[]", nullable: false),
                    nota_cambio = table.Column<string>(type: "text", nullable: true),
                    publicada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publicada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_plantilla_version_plantilla_plantilla_id",
                        column: x => x.plantilla_id,
                        principalSchema: "comparendos",
                        principalTable: "plantilla",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tipologia_empresa",
                schema: "comparendos",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipologia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipologia_empresa", x => new { x.tenant_id, x.tipologia_id });
                    table.ForeignKey(
                        name: "fk_tipologia_empresa_tipologia_dp_tipologia_id",
                        column: x => x.tipologia_id,
                        principalSchema: "comparendos",
                        principalTable: "tipologia_dp",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documento_ocr_campo",
                schema: "comparendos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_ocr_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor_extraido = table.Column<string>(type: "text", nullable: true),
                    confianza = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                    es_null = table.Column<bool>(type: "boolean", nullable: false),
                    valor_corregido = table.Column<string>(type: "text", nullable: true),
                    valor_final = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_ocr_campo", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_ocr_campo_documento_ocr_documento_ocr_id",
                        column: x => x.documento_ocr_id,
                        principalSchema: "comparendos",
                        principalTable: "documento_ocr",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_destinatario_correo",
                schema: "comparendos",
                table: "alerta_destinatario",
                column: "correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archivo_tenant_id_sha256",
                schema: "comparendos",
                table: "archivo",
                columns: new[] { "tenant_id", "sha256" });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_configuracion_entidad_entidad_id_registrado_en",
                schema: "comparendos",
                table: "auditoria_configuracion",
                columns: new[] { "entidad", "entidad_id", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_configuracion_tenant_id_registrado_en",
                schema: "comparendos",
                table: "auditoria_configuracion",
                columns: new[] { "tenant_id", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ux_aviso_conductor_programado",
                schema: "comparendos",
                table: "aviso_conductor",
                columns: new[] { "comparendo_id", "tipo_aviso", "correo_destino" },
                unique: true,
                filter: "disparo = 'Programado'");

            migrationBuilder.CreateIndex(
                name: "ix_cambio_contraventor_solicitud_comparendo_id_numero_intento",
                schema: "comparendos",
                table: "cambio_contraventor_solicitud",
                columns: new[] { "comparendo_id", "numero_intento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cambio_contraventor_abierta",
                schema: "comparendos",
                table: "cambio_contraventor_solicitud",
                column: "comparendo_id",
                unique: true,
                filter: "estado = 'Solicitado'");

            migrationBuilder.CreateIndex(
                name: "ix_carga_masiva_pagos_tenant_id_cargado_en",
                schema: "comparendos",
                table: "carga_masiva_pagos",
                columns: new[] { "tenant_id", "cargado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_carga_masiva_pagos_fila_carga_id_numero_fila",
                schema: "comparendos",
                table: "carga_masiva_pagos_fila",
                columns: new[] { "carga_id", "numero_fila" });

            migrationBuilder.CreateIndex(
                name: "ix_ciclo_ingesta_canal_inicio_en",
                schema: "comparendos",
                table: "ciclo_ingesta",
                columns: new[] { "canal", "inicio_en" });

            migrationBuilder.CreateIndex(
                name: "ux_ciclo_ingesta_en_curso",
                schema: "comparendos",
                table: "ciclo_ingesta",
                columns: new[] { "canal", "tenant_id_manual" },
                unique: true,
                filter: "resultado = 'EnCurso'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_avisos",
                schema: "comparendos",
                table: "comparendo",
                column: "tenant_id",
                filter: "activo AND NOT pagado AND NOT notificado_fisicamente");

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_activo_fecha_limite_gestion",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "activo", "fecha_limite_gestion" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_creado_en",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_estado_cambio_contraventor",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "estado_cambio_contraventor" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_estado_gestion",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "estado_gestion" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_estado_notificacion_conductor",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "estado_notificacion_conductor" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_fecha_imposicion",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "fecha_imposicion" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_fecha_notificacion",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "fecha_notificacion" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_identificacion_monitoreada_id",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "identificacion_monitoreada_id" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_numero_normalizado",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "numero_normalizado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_organismo_transito_id",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "organismo_transito_id" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_placa",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "placa" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_tenant_id_ultimo_evento_en",
                schema: "comparendos",
                table: "comparendo",
                columns: new[] { "tenant_id", "ultimo_evento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_cambio_comparendo_id_registrado_en",
                schema: "comparendos",
                table: "comparendo_cambio",
                columns: new[] { "comparendo_id", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_fuente_comparendo_id_canal_identificacion_monito",
                schema: "comparendos",
                table: "comparendo_fuente",
                columns: new[] { "comparendo_id", "canal", "identificacion_monitoreada_id", "placa_monitoreada_id", "municipio_codigo" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_fuente_presentes_nit",
                schema: "comparendos",
                table: "comparendo_fuente",
                columns: new[] { "tenant_id", "canal", "identificacion_monitoreada_id", "municipio_codigo" },
                filter: "presente");

            migrationBuilder.CreateIndex(
                name: "ix_comparendo_fuente_presentes_placa",
                schema: "comparendos",
                table: "comparendo_fuente",
                columns: new[] { "tenant_id", "canal", "placa_monitoreada_id", "municipio_codigo" },
                filter: "presente");

            migrationBuilder.CreateIndex(
                name: "ix_comunicacion_adjunto_comunicacion_id_archivo_id",
                schema: "comparendos",
                table: "comunicacion_adjunto",
                columns: new[] { "comunicacion_id", "archivo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comunicacion_intento_comunicacion_id",
                schema: "comparendos",
                table: "comunicacion_intento",
                column: "comunicacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_comunicacion_saliente_comparendo_id",
                schema: "comparendos",
                table: "comunicacion_saliente",
                column: "comparendo_id");

            migrationBuilder.CreateIndex(
                name: "ix_comunicacion_saliente_tenant_id_creado_en",
                schema: "comparendos",
                table: "comunicacion_saliente",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_comunicacion_saliente_tenant_id_tipo_estado",
                schema: "comparendos",
                table: "comunicacion_saliente",
                columns: new[] { "tenant_id", "tipo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_consulta_canal_tenant_id_canal",
                schema: "comparendos",
                table: "configuracion_consulta_canal",
                columns: new[] { "tenant_id", "canal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuracion_consultas_estado",
                schema: "comparendos",
                table: "configuracion_consultas",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_consulta_fuente_ciclo_id_canal_clave_consultada_municipio_c",
                schema: "comparendos",
                table: "consulta_fuente",
                columns: new[] { "ciclo_id", "canal", "clave_consultada", "municipio_codigo" });

            migrationBuilder.CreateIndex(
                name: "ix_contraventor_tenant_id_numero_documento",
                schema: "comparendos",
                table: "contraventor",
                columns: new[] { "tenant_id", "numero_documento" });

            migrationBuilder.CreateIndex(
                name: "ix_contraventor_historial_comparendo_id_registrado_en",
                schema: "comparendos",
                table: "contraventor_historial",
                columns: new[] { "comparendo_id", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_derecho_peticion_comparendo_id",
                schema: "comparendos",
                table: "derecho_peticion",
                column: "comparendo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_derecho_peticion_radicado",
                schema: "comparendos",
                table: "derecho_peticion",
                column: "radicado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_derecho_peticion_tenant_id_estado",
                schema: "comparendos",
                table: "derecho_peticion",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_derecho_peticion_tenant_id_fecha_radicacion",
                schema: "comparendos",
                table: "derecho_peticion",
                columns: new[] { "tenant_id", "fecha_radicacion" });

            migrationBuilder.CreateIndex(
                name: "ix_derecho_peticion_tenant_id_respuesta_sentido",
                schema: "comparendos",
                table: "derecho_peticion",
                columns: new[] { "tenant_id", "respuesta_sentido" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_estado_proximo_intento_en",
                schema: "comparendos",
                table: "documento_ocr",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_paquete_id_estado",
                schema: "comparendos",
                table: "documento_ocr",
                columns: new[] { "paquete_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_tenant_id_numero_normalizado",
                schema: "comparendos",
                table: "documento_ocr",
                columns: new[] { "tenant_id", "numero_normalizado" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_campo_documento_ocr_id_nombre_campo",
                schema: "comparendos",
                table: "documento_ocr_campo",
                columns: new[] { "documento_ocr_id", "nombre_campo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_edicion_documento_ocr_id",
                schema: "comparendos",
                table: "documento_ocr_edicion",
                column: "documento_ocr_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_ocr_intento_documento_ocr_id",
                schema: "comparendos",
                table: "documento_ocr_intento",
                column: "documento_ocr_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_soporte_comparendo_id_tipo",
                schema: "comparendos",
                table: "documento_soporte",
                columns: new[] { "comparendo_id", "tipo" });

            migrationBuilder.CreateIndex(
                name: "ix_exportacion_tenant_id_creado_en",
                schema: "comparendos",
                table: "exportacion",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_identificacion_monitoreada_tenant_id_tipo_documento_numero_",
                schema: "comparendos",
                table: "identificacion_monitoreada",
                columns: new[] { "tenant_id", "tipo_documento", "numero_base" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identificacion_monitoreada_tipo_documento_numero_base",
                schema: "comparendos",
                table: "identificacion_monitoreada",
                columns: new[] { "tipo_documento", "numero_base" },
                filter: "activo");

            migrationBuilder.CreateIndex(
                name: "ix_log_migracion_canal_resultado_inicio_en",
                schema: "comparendos",
                table: "log_migracion",
                columns: new[] { "canal", "resultado", "inicio_en" });

            migrationBuilder.CreateIndex(
                name: "ix_log_migracion_ciclo_id",
                schema: "comparendos",
                table: "log_migracion",
                column: "ciclo_id");

            migrationBuilder.CreateIndex(
                name: "ix_log_migracion_tenant_id_inicio_en",
                schema: "comparendos",
                table: "log_migracion",
                columns: new[] { "tenant_id", "inicio_en" });

            migrationBuilder.CreateIndex(
                name: "ix_lote_dp_tenant_id_creado_en",
                schema: "comparendos",
                table: "lote_dp",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_lote_dp_item_lote_id_comparendo_id",
                schema: "comparendos",
                table: "lote_dp_item",
                columns: new[] { "lote_id", "comparendo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_municipio_fuente_codigo_fuente",
                schema: "comparendos",
                table: "municipio_fuente",
                column: "codigo_fuente",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organismo_transito_codigo",
                schema: "comparendos",
                table: "organismo_transito",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organismo_transito_alias_organismo_transito_id",
                schema: "comparendos",
                table: "organismo_transito_alias",
                column: "organismo_transito_id");

            migrationBuilder.CreateIndex(
                name: "ix_organismo_transito_alias_texto_normalizado",
                schema: "comparendos",
                table: "organismo_transito_alias",
                column: "texto_normalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pendientes",
                schema: "comparendos",
                table: "outbox",
                column: "occurred_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pago_comparendo_id",
                schema: "comparendos",
                table: "pago",
                column: "comparendo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pago_tenant_id_fecha_pago",
                schema: "comparendos",
                table: "pago",
                columns: new[] { "tenant_id", "fecha_pago" });

            migrationBuilder.CreateIndex(
                name: "ix_pago_tenant_id_registrado_por_registrado_en",
                schema: "comparendos",
                table: "pago",
                columns: new[] { "tenant_id", "registrado_por", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_paquete_ocr_tenant_id_creado_en",
                schema: "comparendos",
                table: "paquete_ocr",
                columns: new[] { "tenant_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_paquete_ocr_tenant_id_estado",
                schema: "comparendos",
                table: "paquete_ocr",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_permiso_ot_empresa_organismo_transito_id",
                schema: "comparendos",
                table: "permiso_ot_empresa",
                column: "organismo_transito_id");

            migrationBuilder.CreateIndex(
                name: "ix_permiso_ot_empresa_tenant_id_organismo_transito_id_represen",
                schema: "comparendos",
                table: "permiso_ot_empresa",
                columns: new[] { "tenant_id", "organismo_transito_id", "representante_legal_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_placa_monitoreada_placa",
                schema: "comparendos",
                table: "placa_monitoreada",
                column: "placa",
                filter: "activo");

            migrationBuilder.CreateIndex(
                name: "ix_placa_monitoreada_tenant_id_placa",
                schema: "comparendos",
                table: "placa_monitoreada",
                columns: new[] { "tenant_id", "placa" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_conjunto_tipo_tipologia_id",
                schema: "comparendos",
                table: "plantilla",
                columns: new[] { "conjunto", "tipo", "tipologia_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_version_plantilla_id_numero_version",
                schema: "comparendos",
                table: "plantilla_version",
                columns: new[] { "plantilla_id", "numero_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_portal_enlace_comparendo_id",
                schema: "comparendos",
                table: "portal_enlace",
                column: "comparendo_id");

            migrationBuilder.CreateIndex(
                name: "ix_portal_enlace_token_hash",
                schema: "comparendos",
                table: "portal_enlace",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_ocr_version_numero_version",
                schema: "comparendos",
                table: "prompt_ocr_version",
                column: "numero_version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_ocr_version_publicada",
                schema: "comparendos",
                table: "prompt_ocr_version",
                column: "publicada",
                unique: true,
                filter: "publicada");

            migrationBuilder.CreateIndex(
                name: "ux_representante_legal_empresa_activo",
                schema: "comparendos",
                table: "representante_legal",
                column: "tenant_id",
                unique: true,
                filter: "alcance = 'Empresa' AND activo");

            migrationBuilder.CreateIndex(
                name: "ux_representante_legal_nit_activo",
                schema: "comparendos",
                table: "representante_legal",
                columns: new[] { "tenant_id", "identificacion_monitoreada_id" },
                unique: true,
                filter: "alcance = 'Identificacion' AND activo");

            migrationBuilder.CreateIndex(
                name: "ix_timeline_evento_comparendo_id_ocurrido_en",
                schema: "comparendos",
                table: "timeline_evento",
                columns: new[] { "comparendo_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_timeline_evento_tenant_id_tipo_ocurrido_en",
                schema: "comparendos",
                table: "timeline_evento",
                columns: new[] { "tenant_id", "tipo", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_tipologia_dp_codigo",
                schema: "comparendos",
                table: "tipologia_dp",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tipologia_empresa_tipologia_id",
                schema: "comparendos",
                table: "tipologia_empresa",
                column: "tipologia_id");

            migrationBuilder.CreateIndex(
                name: "ix_trabajo_programado_tipo",
                schema: "comparendos",
                table: "trabajo_programado",
                column: "tipo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerta_configuracion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "alerta_destinatario",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "archivo",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "archivo_local",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "auditoria_configuracion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "aviso_conductor",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "calendario_dia",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "cambio_contraventor_solicitud",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "carga_masiva_pagos",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "carga_masiva_pagos_fila",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comparendo_cambio",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comparendo_fuente",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comunicacion_adjunto",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comunicacion_intento",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comunicacion_saliente",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "configuracion_consulta_canal",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "configuracion_empresa",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "consulta_fuente",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "contraventor",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "contraventor_historial",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "derecho_peticion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "documento_ocr_campo",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "documento_ocr_edicion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "documento_ocr_intento",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "documento_soporte",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "exportacion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "identificacion_monitoreada",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "inbox",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "infraccion_catalogo",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "log_migracion",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "lote_dp",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "lote_dp_item",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "municipio_fuente",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "organismo_transito_alias",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "outbox",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "pago",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "permiso_ot_empresa",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "placa_monitoreada",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "plantilla_version",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "portal_enlace",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "prompt_ocr_version",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "radicado_reservado",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "representante_legal",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "scheduler_aviso",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "timeline_evento",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "tipologia_empresa",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "trabajo_programado",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "configuracion_consultas",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "documento_ocr",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "ciclo_ingesta",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "organismo_transito",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "plantilla",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "comparendo",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "tipologia_dp",
                schema: "comparendos");

            migrationBuilder.DropTable(
                name: "paquete_ocr",
                schema: "comparendos");
        }
    }
}
