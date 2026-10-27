using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.PersonaNatural.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>personanatural</c>), del que es dueño su usuario de base (HU #13330). Tiene la
/// outbox y la bandeja de entrada del SDK y las tablas del portal (solicitudes, personas, adjuntos, sesiones, cuotas,
/// consentimientos, historial y auditoría de PII). Nunca toca tablas de otro servicio.
/// </summary>
public sealed class PersonaNaturalDb(DbContextOptions<PersonaNaturalDb> options) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);

        modelBuilder.Entity<SolicitudTramite>(e =>
        {
            e.ToTable("solicitud_tramite", t =>
            {
                t.HasCheckConstraint("ck_solicitud_estado", "estado IN (" + string.Join(", ", EstadoSolicitud.Todos.Select(s => $"'{s}'")) + ")");
                t.HasCheckConstraint("ck_solicitud_vehiculo", "placa IS NOT NULL OR vin IS NOT NULL");
            });
            e.HasKey(s => s.Id).HasName("pk_solicitud_tramite");
            e.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(s => s.TenantId).HasColumnName("tenant_id");
            e.Property(s => s.CodigoPublico).HasColumnName("codigo_publico").HasMaxLength(40).IsRequired();
            e.Property(s => s.TipoTramiteCodigo).HasColumnName("tipo_tramite_codigo").HasMaxLength(80).IsRequired();
            e.Property(s => s.DefinicionVersion).HasColumnName("definicion_version");
            e.Property(s => s.DefinicionSnapshot).HasColumnName("definicion_snapshot").HasColumnType("jsonb").IsRequired();
            e.Property(s => s.OrganismoId).HasColumnName("organismo_id");
            e.Property(s => s.OrganismoNombre).HasColumnName("organismo_nombre").HasMaxLength(200).IsRequired();
            e.Property(s => s.Placa).HasColumnName("placa").HasMaxLength(10);
            e.Property(s => s.Vin).HasColumnName("vin").HasMaxLength(30);
            e.Property(s => s.Estado).HasColumnName("estado").HasMaxLength(30).IsRequired();
            e.Property(s => s.CedulaSolicitanteHash).HasColumnName("cedula_solicitante_hash").IsRequired();
            e.Property(s => s.FieldValues).HasColumnName("field_values").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            e.Property(s => s.Datos).HasColumnName("datos").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            e.Property(s => s.ProcedureInstanceId).HasColumnName("procedure_instance_id");
            e.Property(s => s.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(60);
            e.Property(s => s.EstadoTramite).HasColumnName("estado_tramite").HasMaxLength(30);
            e.Property(s => s.SubsanacionActiva).HasColumnName("subsanacion_activa");
            e.Property(s => s.SubsanacionMotivo).HasColumnName("subsanacion_motivo");
            e.Property(s => s.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
            e.Property(s => s.CorrelationId).HasColumnName("correlation_id");
            e.Property(s => s.ExpiraBorradorEn).HasColumnName("expira_borrador_en");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.Property(s => s.RowVersion).HasColumnName("row_version").HasDefaultValue(1L).IsConcurrencyToken();
            e.HasIndex(s => s.CodigoPublico).IsUnique().HasDatabaseName("uq_solicitud_codigo_publico");
            e.HasIndex(s => new { s.TenantId, s.IdempotencyKey }).IsUnique().HasDatabaseName("uq_solicitud_idem");
            e.HasIndex(s => new { s.TipoTramiteCodigo, s.DefinicionVersion }).HasDatabaseName("ix_solicitud_tipo");
            e.HasIndex(s => s.CedulaSolicitanteHash).HasDatabaseName("ix_solicitud_cedula_hash");
            e.HasIndex(s => s.ProcedureInstanceId).HasDatabaseName("ix_solicitud_procedure_instance");
        });

        modelBuilder.Entity<PersonaSolicitante>(e =>
        {
            e.ToTable("persona_solicitante");
            e.HasKey(p => p.Id).HasName("pk_persona_solicitante");
            e.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(p => p.SolicitudId).HasColumnName("solicitud_id");
            e.Property(p => p.Parte).HasColumnName("parte").HasMaxLength(40).IsRequired();
            e.Property(p => p.Ordinal).HasColumnName("ordinal").HasDefaultValue((short)1);
            e.Property(p => p.TipoDocumento).HasColumnName("tipo_documento").HasMaxLength(10).IsRequired();
            e.Property(p => p.DocumentoCifrado).HasColumnName("documento_cifrado").IsRequired();
            e.Property(p => p.DocumentoHash).HasColumnName("documento_hash").IsRequired();
            e.Property(p => p.NombreCifrado).HasColumnName("nombre_cifrado");
            e.Property(p => p.CorreoCifrado).HasColumnName("correo_cifrado");
            e.Property(p => p.CelularCifrado).HasColumnName("celular_cifrado");
            e.Property(p => p.DireccionCifrada).HasColumnName("direccion_cifrada");
            e.Property(p => p.ValidacionId).HasColumnName("validacion_id");
            e.Property(p => p.ValidacionEstado).HasColumnName("validacion_estado").HasMaxLength(20).HasDefaultValue("sin_solicitar").IsRequired();
            e.Property(p => p.ValidacionVigenteHasta).HasColumnName("validacion_vigente_hasta");
            e.Property(p => p.Reenvios).HasColumnName("reenvios").HasDefaultValue((short)0);
            e.Property(p => p.UltimoReenvioEn).HasColumnName("ultimo_reenvio_en");
            e.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(p => p.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_persona_solicitud");
            e.HasIndex(p => new { p.SolicitudId, p.Parte, p.Ordinal }).IsUnique().HasDatabaseName("uq_persona_solicitud_parte_ordinal");
            e.HasIndex(p => p.DocumentoHash).HasDatabaseName("ix_persona_documento_hash");
        });

        modelBuilder.Entity<Adjunto>(e =>
        {
            e.ToTable("adjunto");
            e.HasKey(a => a.Id).HasName("pk_adjunto");
            e.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(a => a.SolicitudId).HasColumnName("solicitud_id");
            e.Property(a => a.Tipo).HasColumnName("tipo").HasMaxLength(60).IsRequired();
            e.Property(a => a.NombreArchivo).HasColumnName("nombre_archivo").HasMaxLength(255).IsRequired();
            e.Property(a => a.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
            e.Property(a => a.TamanoBytes).HasColumnName("tamano_bytes");
            e.Property(a => a.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            e.Property(a => a.StoragePath).HasColumnName("storage_path").HasMaxLength(500).IsRequired();
            e.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(a => a.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_adjunto_solicitud");
            e.HasIndex(a => a.SolicitudId).HasDatabaseName("ix_adjunto_solicitud");
        });

        modelBuilder.Entity<SesionTramite>(e =>
        {
            e.ToTable("sesion_tramite");
            e.HasKey(s => s.Id).HasName("pk_sesion_tramite");
            e.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(s => s.SolicitudId).HasColumnName("solicitud_id");
            e.Property(s => s.TokenHash).HasColumnName("token_hash").IsRequired();
            e.Property(s => s.IpHash).HasColumnName("ip_hash");
            e.Property(s => s.ExpiraEn).HasColumnName("expira_en");
            e.Property(s => s.Revocada).HasColumnName("revocada");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(s => s.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_sesion_solicitud");
            e.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName("uq_sesion_token_hash");
            e.HasIndex(s => s.SolicitudId).HasDatabaseName("ix_sesion_solicitud");
        });

        modelBuilder.Entity<CuotaConsulta>(e =>
        {
            e.ToTable("cuota_consulta");
            e.HasKey(c => new { c.Clave, c.Dia }).HasName("pk_cuota_consulta");
            e.Property(c => c.Clave).HasColumnName("clave").HasMaxLength(120);
            e.Property(c => c.Dia).HasColumnName("dia");
            e.Property(c => c.Usadas).HasColumnName("usadas");
        });

        modelBuilder.Entity<Consentimiento>(e =>
        {
            e.ToTable("consentimiento");
            e.HasKey(c => c.Id).HasName("pk_consentimiento");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.SolicitudId).HasColumnName("solicitud_id");
            e.Property(c => c.DocumentoHash).HasColumnName("documento_hash").IsRequired();
            e.Property(c => c.TextoVersion).HasColumnName("texto_version").HasMaxLength(40).IsRequired();
            e.Property(c => c.AceptadoEn).HasColumnName("aceptado_en");
            e.Property(c => c.IpHash).HasColumnName("ip_hash");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(c => c.SolicitudId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_consentimiento_solicitud");
            e.HasIndex(c => c.SolicitudId).HasDatabaseName("ix_consentimiento_solicitud");
        });

        modelBuilder.Entity<HistorialEstado>(e =>
        {
            e.ToTable("historial_estado");
            e.HasKey(h => h.Id).HasName("pk_historial_estado");
            e.Property(h => h.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(h => h.SolicitudId).HasColumnName("solicitud_id");
            e.Property(h => h.Desde).HasColumnName("desde").HasMaxLength(30);
            e.Property(h => h.Hasta).HasColumnName("hasta").HasMaxLength(30).IsRequired();
            e.Property(h => h.Origen).HasColumnName("origen").HasMaxLength(10).IsRequired();
            e.Property(h => h.Motivo).HasColumnName("motivo");
            e.Property(h => h.EventId).HasColumnName("event_id");
            e.Property(h => h.OcurrioEn).HasColumnName("ocurrio_en");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(h => h.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_historial_solicitud");
            e.HasIndex(h => new { h.SolicitudId, h.OcurrioEn }).HasDatabaseName("ix_historial_solicitud_fecha");
        });

        // Intentos fallidos de reanudar, por radicado (HU #14035): sin llaves foráneas, porque también cuentan los radicados que no existen.
        modelBuilder.Entity<IntentoReanudar>(e =>
        {
            e.ToTable("intento_reanudar");
            e.HasKey(i => i.Id).HasName("pk_intento_reanudar");
            e.Property(i => i.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(i => i.Clave).HasColumnName("clave").IsRequired();
            e.Property(i => i.OcurrioEn).HasColumnName("ocurrio_en").HasDefaultValueSql("now()");
            e.HasIndex(i => new { i.Clave, i.OcurrioEn }).HasDatabaseName("ix_intento_reanudar_clave_fecha");
        });

        modelBuilder.Entity<ConfiguracionRetencion>(e =>
        {
            e.ToTable("configuracion_retencion", t => t.HasCheckConstraint("ck_configuracion_retencion", "id = 1 AND dias_retencion_borrador >= 1 AND intervalo_minutos >= 1 AND lote >= 1"));
            e.HasKey(c => c.Id).HasName("pk_configuracion_retencion");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.Habilitado).HasColumnName("habilitado");
            e.Property(c => c.DiasRetencionBorrador).HasColumnName("dias_retencion_borrador");
            e.Property(c => c.IntervaloMinutos).HasColumnName("intervalo_minutos");
            e.Property(c => c.Lote).HasColumnName("lote");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
            e.HasData(new ConfiguracionRetencion { Id = 1, UpdatedAt = new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.Zero) });
        });

        // HU #14045: cambios de estado de la identidad de cada parte, sin datos personales.
        modelBuilder.Entity<AuditoriaIdentidad>(e =>
        {
            e.ToTable("auditoria_identidad");
            e.HasKey(a => a.Id).HasName("pk_auditoria_identidad");
            e.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(a => a.SolicitudId).HasColumnName("solicitud_id");
            e.Property(a => a.Parte).HasColumnName("parte").HasMaxLength(40).IsRequired();
            e.Property(a => a.Ordinal).HasColumnName("ordinal").HasDefaultValue((short)1);
            e.Property(a => a.Desde).HasColumnName("desde").HasMaxLength(20);
            e.Property(a => a.Hasta).HasColumnName("hasta").HasMaxLength(20).IsRequired();
            e.Property(a => a.Origen).HasColumnName("origen").HasMaxLength(20).IsRequired();
            e.Property(a => a.EventId).HasColumnName("event_id");
            e.Property(a => a.OcurrioEn).HasColumnName("ocurrio_en").HasDefaultValueSql("now()");
            e.HasOne<SolicitudTramite>().WithMany().HasForeignKey(a => a.SolicitudId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_auditoria_identidad_solicitud");
            e.HasIndex(a => new { a.SolicitudId, a.OcurrioEn }).HasDatabaseName("ix_auditoria_identidad_solicitud_fecha");
        });

        // Solo inserción y sin llaves foráneas: debe sobrevivir a la solicitud que audita.
        modelBuilder.Entity<AccesoPiiLog>(e =>
        {
            e.ToTable("acceso_pii_log");
            e.HasKey(a => a.Id).HasName("pk_acceso_pii_log");
            e.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(a => a.OcurrioEn).HasColumnName("ocurrio_en").HasDefaultValueSql("now()");
            e.Property(a => a.Actor).HasColumnName("actor").HasMaxLength(120).IsRequired();
            e.Property(a => a.Accion).HasColumnName("accion").HasMaxLength(60).IsRequired();
            e.Property(a => a.SolicitudId).HasColumnName("solicitud_id");
            e.Property(a => a.IpHash).HasColumnName("ip_hash");
            e.HasIndex(a => new { a.SolicitudId, a.OcurrioEn }).HasDatabaseName("ix_acceso_pii_solicitud_fecha");
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
