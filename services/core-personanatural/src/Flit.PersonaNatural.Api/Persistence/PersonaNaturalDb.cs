using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.PersonaNatural.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>personanatural</c>), del que es dueño su usuario de base (HU #13330). Tiene la
/// outbox y la bandeja de entrada del SDK y las tablas del portal (solicitudes, personas, adjuntos, sesiones, cuotas,
/// consentimientos, historial y auditoría de PII). Nunca toca tablas de otro servicio.
/// <para>
/// Nombres de base en inglés, snake_case y singular; las propiedades y clases C# conservan su nombre en español y el
/// mapeo vive aquí. Los valores de dominio (estados, orígenes) siguen en español porque son datos, no identificadores.
/// </para>
/// <para>
/// EXCEPCIONES DE CONVENCIÓN DE DATOS (ADR-0075, decisión del PO): (1) sin <c>tenant_id</c> de aislamiento ni RLS: el BFF
/// es un servicio propio con base y usuario propios y un solo tenant; <c>tenant_id</c> solo existe en
/// <c>procedure_request</c> como dato de la solicitud y para la idempotencia por empresa. (2) Sin disparadores
/// <c>row_version</c>/<c>audit_log</c>: la aplicación gestiona <c>row_version</c> (token de concurrencia) y
/// <c>updated_at</c>; la auditoría propia son <c>pii_access_log</c>, <c>identity_audit</c> y <c>status_history</c>.
/// (3) Sin borrado lógico: la retención anonimiza o borra físicamente (Ley 1581). (4) Las FKs llevan
/// <c>ON UPDATE RESTRICT</c> escrito a mano en la migración, porque EF Core no lo modela en el modelo.
/// </para>
/// </summary>
public sealed class PersonaNaturalDb(DbContextOptions<PersonaNaturalDb> options) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;
    public const string SecuenciaCodigoPublico = "public_code_seq";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);

        // HU #14047: consecutivo del radicado visible (PN-AAAA-NNNNNN); una secuencia no se repite entre réplicas ni tras un fallo.
        modelBuilder.HasSequence<long>(SecuenciaCodigoPublico, Schema).StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<SolicitudTramite>(e =>
        {
            e.ToTable("procedure_request", t =>
            {
                t.HasComment("Solicitud de un ciudadano para radicar un trámite en FLIT 2 (portal persona natural).");
                t.HasCheckConstraint("ck_procedure_request_status", "status IN (" + string.Join(", ", EstadoSolicitud.Todos.Select(s => $"'{s}'")) + ")");
                t.HasCheckConstraint("ck_procedure_request_vehicle", "plate IS NOT NULL OR vin IS NOT NULL");
            });
            e.HasKey(s => s.Id).HasName("pk_procedure_request");
            e.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(s => s.TenantId).HasColumnName("tenant_id");
            e.Property(s => s.CodigoPublico).HasColumnName("public_code").HasMaxLength(40).IsRequired();
            e.Property(s => s.TipoTramiteCodigo).HasColumnName("procedure_type_code").HasMaxLength(80).IsRequired();
            e.Property(s => s.DefinicionVersion).HasColumnName("definition_version");
            e.Property(s => s.DefinicionSnapshot).HasColumnName("definition_snapshot").HasColumnType("jsonb").IsRequired();
            e.Property(s => s.OrganismoId).HasColumnName("transit_office_id");
            e.Property(s => s.OrganismoNombre).HasColumnName("transit_office_name").HasMaxLength(200).IsRequired();
            e.Property(s => s.Placa).HasColumnName("plate").HasMaxLength(10);
            e.Property(s => s.Vin).HasColumnName("vin").HasMaxLength(30);
            e.Property(s => s.Estado).HasColumnName("status").HasMaxLength(30).IsRequired();
            e.Property(s => s.CedulaSolicitanteHash).HasColumnName("requester_document_hash").IsRequired()
                .HasComment("@pii:medium Huella HMAC del documento del solicitante (no es el documento); sirve para reanudar el borrador.");
            e.Property(s => s.FieldValues).HasColumnName("field_values").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired()
                .HasComment("@pii:high Valores del formulario; pueden traer datos personales y se vacían al anonimizar el borrador.");
            e.Property(s => s.Datos).HasColumnName("data").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired()
                .HasComment("@pii:high Datos complementarios del trámite; pueden traer datos personales y se vacían al anonimizar el borrador.");
            e.Property(s => s.ProcedureInstanceId).HasColumnName("procedure_instance_id");
            e.Property(s => s.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(60);
            e.Property(s => s.EstadoTramite).HasColumnName("procedure_status").HasMaxLength(30);
            e.Property(s => s.SubsanacionActiva).HasColumnName("rectification_active");
            e.Property(s => s.SubsanacionMotivo).HasColumnName("rectification_reason");
            e.Property(s => s.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
            e.Property(s => s.CorrelationId).HasColumnName("correlation_id");
            e.Property(s => s.ExpiraBorradorEn).HasColumnName("draft_expires_at");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.Property(s => s.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
            e.HasIndex(s => s.CodigoPublico).IsUnique().HasDatabaseName("uq_procedure_request_public_code");
            e.HasIndex(s => new { s.TenantId, s.IdempotencyKey }).IsUnique().HasDatabaseName("uq_procedure_request_idempotency");
            e.HasIndex(s => new { s.TenantId, s.TipoTramiteCodigo, s.DefinicionVersion }).HasDatabaseName("ix_procedure_request_type");
            e.HasIndex(s => s.CedulaSolicitanteHash).HasDatabaseName("ix_procedure_request_requester_document_hash");
            e.HasIndex(s => s.ProcedureInstanceId).HasDatabaseName("ix_procedure_request_procedure_instance");
        });

        modelBuilder.Entity<PersonaSolicitante>(e =>
        {
            e.ToTable("requester_person", t => t.HasComment("Persona de una solicitud (vendedor, comprador, titular…). Datos personales cifrados en la aplicación (AES-GCM)."));
            e.HasKey(p => p.Id).HasName("pk_requester_person");
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(p => p.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(p => p.Parte).HasColumnName("party_role").HasMaxLength(40).IsRequired();
            e.Property(p => p.Ordinal).HasColumnName("ordinal").HasDefaultValue((short)1);
            e.Property(p => p.TipoDocumento).HasColumnName("document_type").HasMaxLength(10).IsRequired();
            e.Property(p => p.DocumentoCifrado).HasColumnName("document_encrypted").IsRequired()
                .HasComment("@pii:high Número de documento cifrado (AES-GCM).");
            e.Property(p => p.DocumentoHash).HasColumnName("document_hash").IsRequired()
                .HasComment("@pii:medium Huella HMAC del documento (no es el documento); sirve para búsquedas por igualdad.");
            e.Property(p => p.NombreCifrado).HasColumnName("name_encrypted")
                .HasComment("@pii:high Nombre completo cifrado (AES-GCM).");
            e.Property(p => p.CorreoCifrado).HasColumnName("email_encrypted")
                .HasComment("@pii:medium Correo electrónico cifrado (AES-GCM).");
            e.Property(p => p.CelularCifrado).HasColumnName("phone_encrypted")
                .HasComment("@pii:medium Celular cifrado (AES-GCM).");
            e.Property(p => p.DireccionCifrada).HasColumnName("address_encrypted")
                .HasComment("@pii:high Dirección cifrada (AES-GCM).");
            e.Property(p => p.CiudadCifrada).HasColumnName("city_encrypted")
                .HasComment("@pii:medium Ciudad cifrada (AES-GCM).");
            e.Property(p => p.ValidacionId).HasColumnName("identity_validation_id");
            e.Property(p => p.ValidacionEstado).HasColumnName("identity_validation_status").HasMaxLength(20).HasDefaultValue("sin_solicitar").IsRequired();
            e.Property(p => p.ValidacionVigenteHasta).HasColumnName("identity_validation_valid_until");
            e.Property(p => p.Reenvios).HasColumnName("resend_count").HasDefaultValue((short)0);
            e.Property(p => p.UltimoReenvioEn).HasColumnName("last_resend_at");
            e.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(p => p.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_requester_person_procedure_request");
            e.HasIndex(p => new { p.SolicitudId, p.Parte, p.Ordinal }).IsUnique().HasDatabaseName("uq_requester_person_request_role_ordinal");
            e.HasIndex(p => p.DocumentoHash).HasDatabaseName("ix_requester_person_document_hash");
        });

        modelBuilder.Entity<Adjunto>(e =>
        {
            e.ToTable("attachment", t => t.HasComment("Archivo adjunto de una solicitud; solo se guarda la referencia opaca al almacenamiento."));
            e.HasKey(a => a.Id).HasName("pk_attachment");
            e.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(a => a.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(a => a.Tipo).HasColumnName("attachment_type").HasMaxLength(60).IsRequired();
            e.Property(a => a.NombreArchivo).HasColumnName("file_name").HasMaxLength(255).IsRequired()
                .HasComment("@pii:medium Nombre del archivo que subió el ciudadano; puede contener su nombre.");
            e.Property(a => a.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
            e.Property(a => a.TamanoBytes).HasColumnName("size_bytes");
            e.Property(a => a.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            e.Property(a => a.StoragePath).HasColumnName("storage_path").HasMaxLength(500).IsRequired();
            e.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(a => a.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_attachment_procedure_request");
            e.HasIndex(a => a.SolicitudId).HasDatabaseName("ix_attachment_procedure_request");
        });

        modelBuilder.Entity<SesionTramite>(e =>
        {
            e.ToTable("procedure_session", t => t.HasComment("Sesión efímera por trámite (el portal no tiene cuentas). Solo se guarda el hash del token."));
            e.HasKey(s => s.Id).HasName("pk_procedure_session");
            e.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(s => s.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(s => s.TokenHash).HasColumnName("token_hash").IsRequired()
                .HasComment("@pii:medium Hash del token de sesión (el token nunca se guarda).");
            e.Property(s => s.IpHash).HasColumnName("ip_hash")
                .HasComment("@pii:medium Huella HMAC de la IP (no es la IP).");
            e.Property(s => s.ExpiraEn).HasColumnName("expires_at");
            e.Property(s => s.Revocada).HasColumnName("revoked");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(s => s.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_procedure_session_procedure_request");
            e.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("uq_procedure_session_token_hash");
            e.HasIndex(s => s.SolicitudId).HasDatabaseName("ix_procedure_session_procedure_request");
        });

        modelBuilder.Entity<CuotaConsulta>(e =>
        {
            e.ToTable("query_quota", t => t.HasComment("Contador diario para los límites por documento, IP y global; persistente para sobrevivir a los reinicios."));
            e.HasKey(c => new { c.Clave, c.Dia }).HasName("pk_query_quota");
            e.Property(c => c.Clave).HasColumnName("quota_key").HasMaxLength(120)
                .HasComment("@pii:medium Clave del contador; puede incluir la huella HMAC del documento o de la IP (no el dato).");
            e.Property(c => c.Dia).HasColumnName("quota_date");
            e.Property(c => c.Usadas).HasColumnName("used_count");
        });

        modelBuilder.Entity<Consentimiento>(e =>
        {
            e.ToTable("consent", t => t.HasComment("Constancia del consentimiento de tratamiento de datos (Ley 1581): texto aceptado, momento y huella de quien acepta."));
            e.HasKey(c => c.Id).HasName("pk_consent");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(c => c.DocumentoHash).HasColumnName("document_hash").IsRequired()
                .HasComment("@pii:medium Huella HMAC del documento de quien acepta (no es el documento).");
            e.Property(c => c.TextoVersion).HasColumnName("text_version").HasMaxLength(40).IsRequired();
            e.Property(c => c.AceptadoEn).HasColumnName("accepted_at");
            e.Property(c => c.IpHash).HasColumnName("ip_hash")
                .HasComment("@pii:medium Huella HMAC de la IP (no es la IP).");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(c => c.SolicitudId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_consent_procedure_request");
            e.HasIndex(c => c.SolicitudId).HasDatabaseName("ix_consent_procedure_request");
        });

        modelBuilder.Entity<HistorialEstado>(e =>
        {
            e.ToTable("status_history", t => t.HasComment("Historial de cambios de estado de una solicitud, tanto del portal como los que llegan de FLIT 2."));
            e.HasKey(h => h.Id).HasName("pk_status_history");
            e.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(h => h.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(h => h.Desde).HasColumnName("from_status").HasMaxLength(30);
            e.Property(h => h.Hasta).HasColumnName("to_status").HasMaxLength(30).IsRequired();
            e.Property(h => h.Origen).HasColumnName("source").HasMaxLength(10).IsRequired();
            e.Property(h => h.Motivo).HasColumnName("reason");
            e.Property(h => h.EventId).HasColumnName("event_id");
            e.Property(h => h.OcurrioEn).HasColumnName("occurred_at");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(h => h.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_status_history_procedure_request");
            e.HasIndex(h => new { h.SolicitudId, h.OcurrioEn }).HasDatabaseName("ix_status_history_request_occurred");
        });

        // Intentos fallidos de reanudar, por radicado (HU #14035): sin llaves foráneas, porque también cuentan los radicados que no existen.
        modelBuilder.Entity<IntentoReanudar>(e =>
        {
            e.ToTable("resume_attempt", t => t.HasComment("Intento fallido de reanudar con radicado y documento; sirve para bloquear el radicado tras varios fallos seguidos."));
            e.HasKey(i => i.Id).HasName("pk_resume_attempt");
            e.Property(i => i.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(i => i.Clave).HasColumnName("code_hash").IsRequired()
                .HasComment("SHA-256 del radicado normalizado, exista o no (no es un dato personal).");
            e.Property(i => i.OcurrioEn).HasColumnName("occurred_at").HasDefaultValueSql("now()");
            e.HasIndex(i => new { i.Clave, i.OcurrioEn }).HasDatabaseName("ix_resume_attempt_code_hash_occurred");
        });

        modelBuilder.Entity<ConfiguracionRetencion>(e =>
        {
            e.ToTable("retention_config", t =>
            {
                t.HasComment("Fila única con la política de retención de borradores; la cadencia vive en la base y se relee en cada ciclo.");
                t.HasCheckConstraint("ck_retention_config", "id = 1 AND draft_retention_days >= 1 AND interval_minutes >= 1 AND batch_size >= 1");
            });
            e.HasKey(c => c.Id).HasName("pk_retention_config");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.Habilitado).HasColumnName("enabled");
            e.Property(c => c.DiasRetencionBorrador).HasColumnName("draft_retention_days");
            e.Property(c => c.IntervaloMinutos).HasColumnName("interval_minutes");
            e.Property(c => c.Lote).HasColumnName("batch_size");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasData(new ConfiguracionRetencion { Id = 1, UpdatedAt = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero) });
        });

        // HU #14045: cambios de estado de la identidad de cada parte, sin datos personales.
        modelBuilder.Entity<AuditoriaIdentidad>(e =>
        {
            e.ToTable("identity_audit", t => t.HasComment("Auditoría de los cambios de estado de la validación de identidad de cada parte. Sin datos personales; solo inserción."));
            e.HasKey(a => a.Id).HasName("pk_identity_audit");
            e.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(a => a.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(a => a.Parte).HasColumnName("party_role").HasMaxLength(40).IsRequired();
            e.Property(a => a.Ordinal).HasColumnName("ordinal").HasDefaultValue((short)1);
            e.Property(a => a.Desde).HasColumnName("from_status").HasMaxLength(20);
            e.Property(a => a.Hasta).HasColumnName("to_status").HasMaxLength(20).IsRequired();
            e.Property(a => a.Origen).HasColumnName("source").HasMaxLength(20).IsRequired();
            e.Property(a => a.EventId).HasColumnName("event_id");
            e.Property(a => a.OcurrioEn).HasColumnName("occurred_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(a => a.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_identity_audit_procedure_request");
            e.HasIndex(a => new { a.SolicitudId, a.OcurrioEn }).HasDatabaseName("ix_identity_audit_request_occurred");
        });

        // Solo inserción y sin llaves foráneas: debe sobrevivir a la solicitud que audita.
        modelBuilder.Entity<AccesoPiiLog>(e =>
        {
            e.ToTable("pii_access_log", t => t.HasComment("Auditoría de acceso a datos personales. Solo inserción, sin llaves foráneas ni disparadores (como el registro de acceso externo, ADR-0067)."));
            e.HasKey(a => a.Id).HasName("pk_pii_access_log");
            e.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(a => a.OcurrioEn).HasColumnName("occurred_at").HasDefaultValueSql("now()");
            e.Property(a => a.Actor).HasColumnName("actor").HasMaxLength(120).IsRequired();
            e.Property(a => a.Accion).HasColumnName("action").HasMaxLength(60).IsRequired();
            e.Property(a => a.SolicitudId).HasColumnName("procedure_request_id");
            e.Property(a => a.IpHash).HasColumnName("ip_hash")
                .HasComment("@pii:medium Huella HMAC de la IP (no es la IP).");
            e.HasIndex(a => new { a.SolicitudId, a.OcurrioEn }).HasDatabaseName("ix_pii_access_log_request_occurred");
        });
    }

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        });
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class PersonaNaturalDbDesignFactory : IDesignTimeDbContextFactory<PersonaNaturalDb>
{
    public PersonaNaturalDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PersonaNaturalDb>();
        PersonaNaturalDb.Configure(options, "Host=localhost;Database=diseno");
        return new PersonaNaturalDb(options.Options);
    }
}
