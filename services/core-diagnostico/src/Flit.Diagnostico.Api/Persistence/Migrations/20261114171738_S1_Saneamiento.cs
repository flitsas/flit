using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Diagnostico.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class S1_Saneamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notificaciones_tipo",
                schema: "diagnostico",
                table: "notificaciones");

            migrationBuilder.AddColumn<string>(
                name: "saneamiento_estado",
                schema: "diagnostico",
                table: "vehiculos",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                comment: "Estado del saneamiento no anulado del vehículo (copia); null si no hay. Lo escribe solo EscritorSaneamiento, en la transacción de la transición. [ADR-0087]");

            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                schema: "diagnostico",
                table: "notificaciones",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA, LOTE_TERMINADO o SANEAMIENTO_TOMADO/CERRADO/REABIERTO/ANULADO (D8: sin DICTAMEN_AJUSTADO; ADR-0087).",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA o LOTE_TERMINADO (D8: sin DICTAMEN_AJUSTADO).");

            migrationBuilder.AlterColumn<Guid>(
                name: "referencia_id",
                schema: "diagnostico",
                table: "notificaciones",
                type: "uuid",
                nullable: false,
                comment: "Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO), saneamiento_historial.id (SANEAMIENTO_*, ADR-0087). Sin FK (polimórfica, ADR-0086).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO). Sin FK (polimórfica, ADR-0086).");

            migrationBuilder.CreateTable(
                name: "saneamientos",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del lote y del vehículo."),
                    diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lote del vehículo."),
                    vehiculo_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Vehículo a sanear."),
                    estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "SOLICITADO, EN_GESTION, SANEADO, NO_PROCEDE o ANULADO (final)."),
                    solicitado_por = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario de la compañía que marcó (claim sub, seudónimo)."),
                    solicitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante de la solicitud (timestamptz). Orden de la bandeja."),
                    tomado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario FLIT que lo tomó por primera vez; no cambia al reabrir."),
                    tomado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante de la primera toma; no cambia al reabrir."),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario FLIT que cerró; se limpia al reabrir."),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante del cierre (SANEADO o NO_PROCEDE); se limpia al reabrir."),
                    motivo_cierre = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii:low Motivo de NO_PROCEDE (texto de FLIT, 10..500); visible al cliente; nunca en logs ni avisos."),
                    anulado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Quien anuló: el cliente (desmarcar) o FLIT."),
                    anulado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante de la anulación."),
                    anulado_por_flit = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "false", comment: "True si anuló FLIT (exige motivo); false si desmarcó el cliente o no está anulado."),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii:low Motivo de la anulación de FLIT (10..500); visible al cliente; nunca en logs ni avisos."),
                    observacion_flit = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "@pii:low Observación general de FLIT, visible al cliente; editable en EN_GESTION."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saneamientos", x => x.id);
                    table.UniqueConstraint("uq_saneamientos_tenant_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_saneamientos_anulacion", "(anulado_por IS NULL) = (anulado_en IS NULL) AND (estado = 'ANULADO') = (anulado_en IS NOT NULL) AND anulado_por_flit = (motivo_anulacion IS NOT NULL) AND (NOT anulado_por_flit OR anulado_en IS NOT NULL)");
                    table.CheckConstraint("ck_saneamientos_cierre", "(cerrado_por IS NULL) = (cerrado_en IS NULL) AND (estado IN ('SANEADO', 'NO_PROCEDE')) = (cerrado_en IS NOT NULL)");
                    table.CheckConstraint("ck_saneamientos_estado", "estado IN ('SOLICITADO', 'EN_GESTION', 'SANEADO', 'NO_PROCEDE', 'ANULADO')");
                    table.CheckConstraint("ck_saneamientos_motivo_anulacion", "motivo_anulacion IS NULL OR char_length(btrim(motivo_anulacion)) BETWEEN 10 AND 500");
                    table.CheckConstraint("ck_saneamientos_motivo_cierre", "(estado = 'NO_PROCEDE') = (motivo_cierre IS NOT NULL) AND (motivo_cierre IS NULL OR char_length(btrim(motivo_cierre)) BETWEEN 10 AND 500)");
                    table.CheckConstraint("ck_saneamientos_observacion", "observacion_flit IS NULL OR btrim(observacion_flit) <> ''");
                    table.CheckConstraint("ck_saneamientos_tomado", "(tomado_por IS NULL) = (tomado_en IS NULL) AND (estado <> 'SOLICITADO' OR tomado_en IS NULL) AND (estado NOT IN ('EN_GESTION', 'SANEADO', 'NO_PROCEDE') OR tomado_en IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_saneamientos_diagnosticos",
                        columns: x => new { x.tenant_id, x.diagnostico_id },
                        principalSchema: "diagnostico",
                        principalTable: "diagnosticos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_saneamientos_vehiculos",
                        columns: x => new { x.tenant_id, x.vehiculo_id },
                        principalSchema: "diagnostico",
                        principalTable: "vehiculos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Saneamiento de un vehículo (clase (a) de ADR-0072, ADR-0087): solicitud del cliente y gestión de FLIT, sin dinero. Un solo no anulado por vehículo. Sin borrado: ANULADO es un estado.");

            migrationBuilder.CreateTable(
                name: "saneamiento_historial",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación; referencia_id de los avisos SANEAMIENTO_*."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del saneamiento (también cuando actúa FLIT)."),
                    saneamiento_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saneamiento del evento."),
                    evento = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, comment: "SOLICITADO, DESMARCADO, TOMADO, RENGLON_*, EVIDENCIA_*, OBSERVACION_EDITADA, CERRADO_*, REABIERTO o ANULADO_FLIT."),
                    estado_anterior = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "Estado de partida en las transiciones; null en SOLICITADO y en los eventos de edición."),
                    estado_nuevo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "Estado de llegada en las transiciones; null en los eventos de edición."),
                    renglon_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Renglón del evento; sin FK porque el renglón se puede quitar (ADR-0087)."),
                    fuente = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true, comment: "Copia de la fuente del renglón, legible aunque el renglón ya no esté."),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Actor (claim sub, seudónimo). Al cliente solo se le muestra COMPANIA o FLIT."),
                    es_flit = table.Column<bool>(type: "boolean", nullable: false, comment: "True si actuó por operación FLIT."),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii:low Motivo de NO_PROCEDE, reapertura o anulación FLIT (10..500); nunca en logs ni avisos."),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante del evento (timestamptz).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saneamiento_historial", x => x.id);
                    table.CheckConstraint("ck_saneamiento_historial_estado_anterior", "estado_anterior IS NULL OR estado_anterior IN ('SOLICITADO', 'EN_GESTION', 'SANEADO', 'NO_PROCEDE', 'ANULADO')");
                    table.CheckConstraint("ck_saneamiento_historial_estado_nuevo", "estado_nuevo IS NULL OR estado_nuevo IN ('SOLICITADO', 'EN_GESTION', 'SANEADO', 'NO_PROCEDE', 'ANULADO')");
                    table.CheckConstraint("ck_saneamiento_historial_evento", "evento IN ('SOLICITADO', 'DESMARCADO', 'TOMADO', 'RENGLON_AGREGADO', 'RENGLON_QUITADO', 'RENGLON_ACTUALIZADO', 'EVIDENCIA_CARGADA', 'EVIDENCIA_QUITADA', 'OBSERVACION_EDITADA', 'CERRADO_SANEADO', 'CERRADO_NO_PROCEDE', 'REABIERTO', 'ANULADO_FLIT')");
                    table.CheckConstraint("ck_saneamiento_historial_fuente", "fuente IS NULL OR fuente IN ('SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'IMPUESTO', 'SOAT', 'RTM', 'OTRO')");
                    table.CheckConstraint("ck_saneamiento_historial_motivo", "(evento IN ('CERRADO_NO_PROCEDE', 'REABIERTO', 'ANULADO_FLIT')) = (motivo IS NOT NULL) AND (motivo IS NULL OR char_length(btrim(motivo)) BETWEEN 10 AND 500)");
                    table.CheckConstraint("ck_saneamiento_historial_renglon", "(evento IN ('RENGLON_AGREGADO', 'RENGLON_QUITADO', 'RENGLON_ACTUALIZADO', 'EVIDENCIA_CARGADA', 'EVIDENCIA_QUITADA')) = (renglon_id IS NOT NULL) AND (renglon_id IS NULL) = (fuente IS NULL)");
                    table.CheckConstraint("ck_saneamiento_historial_transicion", "(evento IN ('SOLICITADO', 'DESMARCADO', 'TOMADO', 'CERRADO_SANEADO', 'CERRADO_NO_PROCEDE', 'REABIERTO', 'ANULADO_FLIT')) = (estado_nuevo IS NOT NULL) AND (estado_anterior IS NULL) = (evento = 'SOLICITADO' OR estado_nuevo IS NULL)");
                    table.ForeignKey(
                        name: "fk_saneamiento_historial_saneamientos",
                        columns: x => new { x.tenant_id, x.saneamiento_id },
                        principalSchema: "diagnostico",
                        principalTable: "saneamientos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Eventos del circuito de saneamiento (clase (b) de ADR-0072, ADR-0087): auditoría con actor e instante; renglon_id sin FK. Solo inserción.");

            migrationBuilder.CreateTable(
                name: "saneamiento_renglones",
                schema: "diagnostico",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "uuid v7 generado por la aplicación."),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Empresa dueña del saneamiento."),
                    saneamiento_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Saneamiento al que pertenece."),
                    fuente = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, comment: "SIMIT_VEHICULO, SIMIT_PROPIETARIO, SIMIT_LOCATARIO, IMPUESTO, SOAT, RTM u OTRO. Una sola no quitada por fuente salvo OTRO."),
                    hallazgo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "DEUDA, NO_VIGENTE o SIN_VERIFICAR; foto al tomar (no se regenera). Null en OTRO."),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "@pii:low Descripción de un renglón OTRO (texto de FLIT, 3..200)."),
                    automatico = table.Column<bool>(type: "boolean", nullable: false, comment: "True si lo generó el sistema al tomar; false en OTRO."),
                    resultado = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, defaultValueSql: "'PENDIENTE'", comment: "PENDIENTE, SANEADO o NO_PROCEDE."),
                    nota = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "@pii:low Nota de FLIT, visible al cliente; nunca en logs."),
                    evidencia_referencia = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "@pii:none Referencia del archivo en el almacén (el PDF puede traer datos de terceros, C21); nunca se expone ni se loguea."),
                    evidencia_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "@pii:low Nombre original del PDF, saneado (puede traer un nombre de persona); nunca en logs."),
                    evidencia_sha256 = table.Column<string>(type: "char(64)", nullable: true, comment: "@pii:none SHA-256 hex del PDF; nunca se expone."),
                    evidencia_tamano = table.Column<long>(type: "bigint", nullable: true, comment: "@pii:none Tamaño del PDF en bytes."),
                    evidencia_subida_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Usuario FLIT que cargó el PDF vigente."),
                    evidencia_subida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "@pii:none Instante de carga del PDF vigente."),
                    evidencia_reemplazos = table.Column<short>(type: "smallint", nullable: false, defaultValueSql: "0", comment: "@pii:none Veces que se reemplazó el PDF del renglón (0..20, SP-19)."),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false, comment: "@pii:low Usuario FLIT que tomó (automáticos) o agregó (OTRO)."),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Instante de creación del renglón (timestamptz)."),
                    actualizado_por = table.Column<Guid>(type: "uuid", nullable: true, comment: "@pii:low Último usuario FLIT que cambió resultado, nota o evidencia."),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Instante del último cambio; null si nunca cambió."),
                    quitado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Borrado lógico (SP-2): instante en que FLIT quitó el renglón; null si sigue. Las lecturas filtran quitado_en IS NULL; quién lo quitó queda en saneamiento_historial."),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saneamiento_renglones", x => x.id);
                    table.CheckConstraint("ck_saneamiento_renglones_actualizado", "(actualizado_por IS NULL) = (actualizado_en IS NULL)");
                    table.CheckConstraint("ck_saneamiento_renglones_evidencia", "(evidencia_referencia IS NULL) = (evidencia_nombre IS NULL) AND (evidencia_referencia IS NULL) = (evidencia_sha256 IS NULL) AND (evidencia_referencia IS NULL) = (evidencia_tamano IS NULL) AND (evidencia_referencia IS NULL) = (evidencia_subida_por IS NULL) AND (evidencia_referencia IS NULL) = (evidencia_subida_en IS NULL)");
                    table.CheckConstraint("ck_saneamiento_renglones_evidencia_sha256", "evidencia_sha256 IS NULL OR evidencia_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_saneamiento_renglones_evidencia_tamano", "evidencia_tamano IS NULL OR evidencia_tamano > 0");
                    table.CheckConstraint("ck_saneamiento_renglones_fuente", "fuente IN ('SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'IMPUESTO', 'SOAT', 'RTM', 'OTRO')");
                    table.CheckConstraint("ck_saneamiento_renglones_hallazgo", "hallazgo IS NULL OR hallazgo IN ('DEUDA', 'NO_VIGENTE', 'SIN_VERIFICAR')");
                    table.CheckConstraint("ck_saneamiento_renglones_hallazgo_fuente", "hallazgo IS NULL OR (hallazgo = 'DEUDA' AND fuente IN ('SIMIT_VEHICULO', 'SIMIT_PROPIETARIO', 'SIMIT_LOCATARIO', 'IMPUESTO')) OR (hallazgo = 'SIN_VERIFICAR' AND fuente = 'SIMIT_LOCATARIO') OR (hallazgo = 'NO_VIGENTE' AND fuente IN ('SOAT', 'RTM'))");
                    table.CheckConstraint("ck_saneamiento_renglones_nota", "nota IS NULL OR btrim(nota) <> ''");
                    table.CheckConstraint("ck_saneamiento_renglones_otro", "(fuente = 'OTRO') = (hallazgo IS NULL) AND (fuente = 'OTRO') = (descripcion IS NOT NULL) AND automatico = (fuente <> 'OTRO') AND (descripcion IS NULL OR char_length(btrim(descripcion)) BETWEEN 3 AND 200)");
                    table.CheckConstraint("ck_saneamiento_renglones_reemplazos", "evidencia_reemplazos BETWEEN 0 AND 20");
                    table.CheckConstraint("ck_saneamiento_renglones_resultado", "resultado IN ('PENDIENTE', 'SANEADO', 'NO_PROCEDE')");
                    table.ForeignKey(
                        name: "fk_saneamiento_renglones_saneamientos",
                        columns: x => new { x.tenant_id, x.saneamiento_id },
                        principalSchema: "diagnostico",
                        principalTable: "saneamientos",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Renglón de un saneamiento por fuente (clase (a) de ADR-0072, ADR-0087): hallazgo, resultado, nota y PDF opcional de evidencia. OTRO lo agrega FLIT. Se quita con borrado lógico (quitado_en).");

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_identificador_prefijo",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "identificador_norm" })
                .Annotation("Npgsql:IndexOperators", new[] { "uuid_ops", "varchar_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_vehiculos_saneamiento",
                schema: "diagnostico",
                table: "vehiculos",
                columns: new[] { "tenant_id", "saneamiento_estado" },
                filter: "saneamiento_estado IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_saneamiento_estado",
                schema: "diagnostico",
                table: "vehiculos",
                sql: "saneamiento_estado IS NULL OR saneamiento_estado IN ('SOLICITADO', 'EN_GESTION', 'SANEADO', 'NO_PROCEDE')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notificaciones_tipo",
                schema: "diagnostico",
                table: "notificaciones",
                sql: "tipo IN ('ZIP_EVIDENCIAS_LISTO', 'ZIP_EVIDENCIAS_FALLIDO', 'DERIVACION_TERMINADA', 'LOTE_TERMINADO', 'SANEAMIENTO_TOMADO', 'SANEAMIENTO_CERRADO', 'SANEAMIENTO_REABIERTO', 'SANEAMIENTO_ANULADO')");

            migrationBuilder.CreateIndex(
                name: "ix_saneamiento_historial_saneamiento",
                schema: "diagnostico",
                table: "saneamiento_historial",
                columns: new[] { "tenant_id", "saneamiento_id", "ocurrido_en", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_saneamiento_renglones_saneamiento",
                schema: "diagnostico",
                table: "saneamiento_renglones",
                columns: new[] { "tenant_id", "saneamiento_id" });

            migrationBuilder.CreateIndex(
                name: "ux_saneamiento_renglones_fuente",
                schema: "diagnostico",
                table: "saneamiento_renglones",
                columns: new[] { "tenant_id", "saneamiento_id", "fuente" },
                unique: true,
                filter: "fuente <> 'OTRO' AND quitado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_saneamientos_bandeja",
                schema: "diagnostico",
                table: "saneamientos",
                columns: new[] { "estado", "solicitado_en", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_saneamientos_diagnostico",
                schema: "diagnostico",
                table: "saneamientos",
                columns: new[] { "tenant_id", "diagnostico_id" });

            migrationBuilder.CreateIndex(
                name: "ix_saneamientos_vehiculo",
                schema: "diagnostico",
                table: "saneamientos",
                columns: new[] { "tenant_id", "vehiculo_id" });

            migrationBuilder.CreateIndex(
                name: "ux_saneamiento_vigente",
                schema: "diagnostico",
                table: "saneamientos",
                columns: new[] { "tenant_id", "vehiculo_id" },
                unique: true,
                filter: "estado <> 'ANULADO'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Guarda del rollback (schema.md §4, patrón de R7/R9): bajar las tablas borraría la auditoría del circuito y el CHECK viejo
            // de notificaciones no admitiría los avisos SANEAMIENTO_*.
            migrationBuilder.Sql(@"DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM diagnostico.saneamientos)
     OR EXISTS (SELECT 1 FROM diagnostico.notificaciones WHERE tipo LIKE 'SANEAMIENTO\_%') THEN
    RAISE EXCEPTION 'Rollback de S1_Saneamiento bloqueado: hay saneamientos o avisos de saneamiento. Exportar y decidir con el LT antes del rollback.';
  END IF;
END $$;");

            migrationBuilder.DropTable(
                name: "saneamiento_historial",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "saneamiento_renglones",
                schema: "diagnostico");

            migrationBuilder.DropTable(
                name: "saneamientos",
                schema: "diagnostico");

            migrationBuilder.DropIndex(
                name: "ix_vehiculos_identificador_prefijo",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "ix_vehiculos_saneamiento",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_saneamiento_estado",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notificaciones_tipo",
                schema: "diagnostico",
                table: "notificaciones");

            migrationBuilder.DropColumn(
                name: "saneamiento_estado",
                schema: "diagnostico",
                table: "vehiculos");

            migrationBuilder.AlterColumn<string>(
                name: "tipo",
                schema: "diagnostico",
                table: "notificaciones",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA o LOTE_TERMINADO (D8: sin DICTAMEN_AJUSTADO).",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "ZIP_EVIDENCIAS_LISTO, ZIP_EVIDENCIAS_FALLIDO, DERIVACION_TERMINADA, LOTE_TERMINADO o SANEAMIENTO_TOMADO/CERRADO/REABIERTO/ANULADO (D8: sin DICTAMEN_AJUSTADO; ADR-0087).");

            migrationBuilder.AlterColumn<Guid>(
                name: "referencia_id",
                schema: "diagnostico",
                table: "notificaciones",
                type: "uuid",
                nullable: false,
                comment: "Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO). Sin FK (polimórfica, ADR-0086).",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Id del hecho según tipo: evidencias.id (ZIP_*), derivacion_operaciones.id (DERIVACION_TERMINADA), historial_estado_diagnostico.id (LOTE_TERMINADO), saneamiento_historial.id (SANEAMIENTO_*, ADR-0087). Sin FK (polimórfica, ADR-0086).");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notificaciones_tipo",
                schema: "diagnostico",
                table: "notificaciones",
                sql: "tipo IN ('ZIP_EVIDENCIAS_LISTO', 'ZIP_EVIDENCIAS_FALLIDO', 'DERIVACION_TERMINADA', 'LOTE_TERMINADO')");
        }
    }
}
