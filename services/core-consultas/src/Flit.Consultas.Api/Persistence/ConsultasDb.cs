using Flit.Platform.Sdk.Messaging;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>consultas</c>), del que es dueño su usuario de base (HU #13330). Nace con la
/// outbox y la bandeja de entrada del SDK; las tablas propias se agregan aquí con su migración.
/// </summary>
public sealed class ConsultasDb(DbContextOptions<ConsultasDb> options) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = ServicioSettings.Codigo;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);

        modelBuilder.Entity<ConfiguracionEmpresa>(e =>
        {
            e.ToTable("configuracion_empresa");
            e.HasKey(c => c.TenantId).HasName("pk_configuracion_empresa");
            e.Property(c => c.TenantId).HasColumnName("tenant_id").ValueGeneratedNever();
            e.Property(c => c.CadenasJson).HasColumnName("cadenas").HasColumnType("jsonb");
            e.Property(c => c.FailoverTimeoutMs).HasColumnName("failover_timeout_ms");
            e.Property(c => c.FuenteMultas).HasColumnName("fuente_multas").HasMaxLength(20).IsRequired();
            e.Property(c => c.AvaluosJson).HasColumnName("avaluos").HasColumnType("jsonb");
            e.Property(c => c.ActualizadoEn).HasColumnName("actualizado_en");
        });

        modelBuilder.Entity<ConsumoConsulta>(e =>
        {
            e.ToTable("consumo");
            e.HasKey(c => c.Id).HasName("pk_consumo");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.TenantId).HasColumnName("tenant_id");
            e.Property(c => c.Producto).HasColumnName("producto").HasMaxLength(40).IsRequired();
            e.Property(c => c.Fuente).HasColumnName("fuente").HasMaxLength(20).IsRequired();
            e.Property(c => c.Proveedor).HasColumnName("proveedor").HasMaxLength(60).IsRequired();
            e.Property(c => c.Resultado).HasColumnName("resultado").HasMaxLength(20).IsRequired();
            e.Property(c => c.DesdeCache).HasColumnName("desde_cache");
            e.Property(c => c.LatenciaMs).HasColumnName("latencia_ms");
            e.Property(c => c.OcurridoEn).HasColumnName("ocurrido_en");
            e.HasIndex(c => new { c.TenantId, c.OcurridoEn }).HasDatabaseName("ix_consumo_empresa_fecha");
        });

        // HU #13351: validaciones de Kyverum Verify y avisos recibidos.
        modelBuilder.Entity<ValidacionKyverum>(e =>
        {
            e.ToTable("validaciones_kyverum");
            e.HasKey(v => v.ValidacionId).HasName("pk_validaciones_kyverum");
            e.Property(v => v.ValidacionId).HasColumnName("validacion_id").ValueGeneratedNever();
            e.Property(v => v.TenantId).HasColumnName("tenant_id");
            e.Property(v => v.Producto).HasColumnName("producto").HasMaxLength(40).IsRequired();
            e.Property(v => v.VerificationId).HasColumnName("verification_id").HasMaxLength(100).IsRequired();
            e.Property(v => v.SecretoCifrado).HasColumnName("secreto_cifrado");
            e.Property(v => v.CreadaEn).HasColumnName("creada_en");
        });

        modelBuilder.Entity<AvisoProveedor>(e =>
        {
            e.ToTable("avisos");
            e.HasKey(a => a.Id).HasName("pk_avisos");
            e.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(a => a.Proveedor).HasColumnName("proveedor").HasMaxLength(40).IsRequired();
            e.Property(a => a.ReferenciaId).HasColumnName("referencia_id");
            e.Property(a => a.TenantId).HasColumnName("tenant_id");
            e.Property(a => a.Resultado).HasColumnName("resultado").HasMaxLength(30).IsRequired();
            e.Property(a => a.Cuerpo).HasColumnName("cuerpo").IsRequired();
            e.Property(a => a.RecibidoEn).HasColumnName("recibido_en");
            e.HasIndex(a => new { a.ReferenciaId, a.RecibidoEn }).HasDatabaseName("ix_avisos_referencia");
        });

        // Llaves de Data Protection del servicio (cifran los secretos de los avisos), en su esquema.
        modelBuilder.Entity<DataProtectionKey>(e => e.ToTable("data_protection_keys"));
    }

    public DbSet<ConsumoConsulta> Consumos => Set<ConsumoConsulta>();

    public DbSet<ValidacionKyverum> ValidacionesKyverum => Set<ValidacionKyverum>();

    public DbSet<AvisoProveedor> Avisos => Set<AvisoProveedor>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<ConfiguracionEmpresa> ConfiguracionEmpresas => Set<ConfiguracionEmpresa>();

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        });
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class ConsultasDbDesignFactory : IDesignTimeDbContextFactory<ConsultasDb>
{
    public ConsultasDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ConsultasDb>();
        ConsultasDb.Configure(options, "Host=localhost;Database=diseno");
        return new ConsultasDb(options.Options);
    }
}
