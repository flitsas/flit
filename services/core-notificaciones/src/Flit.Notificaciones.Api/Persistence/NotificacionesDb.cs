using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Notificaciones.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>notificaciones</c>), del que es dueño su usuario de base (HU #13330). Nace con la
/// outbox y la bandeja de entrada del SDK; las tablas propias se agregan aquí con su migración.
/// </summary>
public sealed class NotificacionesDb(DbContextOptions<NotificacionesDb> options) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);

        // HU #13353: registro de entregas.
        modelBuilder.Entity<Entrega>(e =>
        {
            e.ToTable("entregas");
            e.HasKey(x => x.Id).HasName("pk_entregas");
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Plantilla).HasColumnName("plantilla").HasMaxLength(100).IsRequired();
            e.Property(x => x.Canal).HasColumnName("canal").HasMaxLength(30).IsRequired();
            e.Property(x => x.Destinatario).HasColumnName("destinatario").HasMaxLength(320).IsRequired();
            e.Property(x => x.Resultado).HasColumnName("resultado").HasMaxLength(20).IsRequired();
            e.Property(x => x.Desenlace).HasColumnName("desenlace").HasMaxLength(40).IsRequired();
            e.Property(x => x.MotivoFallo).HasColumnName("motivo_fallo").HasMaxLength(1000);
            e.Property(x => x.DuracionMs).HasColumnName("duracion_ms");
            e.Property(x => x.Desviado).HasColumnName("desviado");
            e.Property(x => x.Tema).HasColumnName("tema").HasMaxLength(10);
            e.Property(x => x.TemaVersion).HasColumnName("tema_version");
            e.Property(x => x.RemitenteNombre).HasColumnName("remitente_nombre").HasMaxLength(80);
            e.Property(x => x.Origen).HasColumnName("origen").HasMaxLength(40).IsRequired();
            e.Property(x => x.TrabajoId).HasColumnName("trabajo_id");
            e.Property(x => x.OcurridoEn).HasColumnName("ocurrido_en");
            e.HasIndex(x => new { x.TenantId, x.OcurridoEn }).HasDatabaseName("ix_entregas_empresa_fecha");
        });
    }

    public DbSet<Entrega> Entregas => Set<Entrega>();

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        });
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class NotificacionesDbDesignFactory : IDesignTimeDbContextFactory<NotificacionesDb>
{
    public NotificacionesDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NotificacionesDb>();
        NotificacionesDb.Configure(options, "Host=localhost;Database=diseno");
        return new NotificacionesDb(options.Options);
    }
}
