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

        // HU #13348: valores de avalúo del modo mock, antes en tramites.avaluo_mock_values.
        modelBuilder.Entity<DepartamentoConsultaImpuesto>(e =>
        {
            e.ToTable("vehicle_tax_departments", t => t.HasComment(
                "Catálogo global de departamentos con consulta de impuesto. Sin tenant_id: ADR-0080. Lista efectiva: is_active y deleted_at nulo."));
            e.HasKey(d => d.Id).HasName("pk_vehicle_tax_departments");
            e.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(d => d.DepartmentCode).HasColumnName("department_code").HasMaxLength(5).IsRequired();
            e.Property(d => d.DepartmentName).HasColumnName("department_name").HasMaxLength(50).IsRequired();
            e.Property(d => d.IsActive).HasColumnName("is_active");
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
            e.Property(d => d.UpdatedAt).HasColumnName("updated_at");
            e.Property(d => d.DeletedAt).HasColumnName("deleted_at");
            e.Property(d => d.ExternalRefs).HasColumnName("external_refs").HasColumnType("jsonb");
            e.HasIndex(d => d.DepartmentCode).IsUnique().HasDatabaseName("uq_vehicle_tax_departments_department_code");
        });

        modelBuilder.Entity<TrabajoImpuestoExterno>(e =>
        {
            e.ToTable("vehicle_tax_jobs", t => t.HasComment(
                "Seguimiento TrabajoId del bus -> id de la consulta asíncrona de impuesto (D4). Sin placa, documento ni respuesta. Retención 24 h."));
            e.HasKey(t => t.Id).HasName("pk_vehicle_tax_jobs");
            e.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(t => t.TrabajoId).HasColumnName("trabajo_id");
            e.Property(t => t.TenantId).HasColumnName("tenant_id");
            e.Property(t => t.DepartmentCode).HasColumnName("department_code").HasMaxLength(5).IsRequired();
            e.Property(t => t.ExternalId).HasColumnName("external_id").HasMaxLength(64);
            e.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(t => t.SubmittedAt).HasColumnName("submitted_at");
            e.Property(t => t.LastPolledAt).HasColumnName("last_polled_at");
            e.Property(t => t.PollCount).HasColumnName("poll_count");
            e.Property(t => t.CompletedAt).HasColumnName("completed_at");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.Property(t => t.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(t => t.TrabajoId).IsUnique().HasDatabaseName("uq_vehicle_tax_jobs_trabajo_id");
            e.HasIndex(t => new { t.TenantId, t.CreatedAt }).HasDatabaseName("ix_vehicle_tax_jobs_tenant_id_created_at");
            e.HasIndex(t => t.CreatedAt).HasDatabaseName("ix_vehicle_tax_jobs_created_at");
            // HU #14711: el receptor del aviso busca la consulta por (empresa, id externo).
            e.HasIndex(t => new { t.TenantId, t.ExternalId }).HasDatabaseName("ix_vehicle_tax_jobs_tenant_id_external_id");
            e.ToTable(t => t.HasCheckConstraint("ck_vehicle_tax_jobs_status",
                "status IN ('reserved','queued','running','succeeded','partial','failed','timeout','error')"));
        });

        // HU #14711: deduplicación de los avisos del proveedor de impuesto, por (empresa, entrega). Sin cuerpo ni PII; purga a 7 días.
        modelBuilder.Entity<EntregaAvisoImpuesto>(e =>
        {
            e.ToTable("vehicle_tax_webhook_deliveries", t => t.HasComment(
                "Avisos del proveedor de impuesto ya verificados, para deduplicar por (tenant_id, delivery_id) (HU #14711). Sin cuerpo, firma ni PII. Retención 7 días."));
            e.HasKey(d => d.Id).HasName("pk_vehicle_tax_webhook_deliveries");
            e.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(d => d.TenantId).HasColumnName("tenant_id");
            e.Property(d => d.DeliveryId).HasColumnName("delivery_id").HasMaxLength(120).IsRequired();
            e.Property(d => d.ExternalId).HasColumnName("external_id").HasMaxLength(64).IsRequired();
            e.Property(d => d.Attempt).HasColumnName("attempt");
            e.Property(d => d.CreatedAt).HasColumnName("created_at");
            e.HasIndex(d => new { d.TenantId, d.DeliveryId }).IsUnique().HasDatabaseName("uq_vehicle_tax_webhook_deliveries_tenant_id_delivery_id");
            e.HasIndex(d => d.CreatedAt).HasDatabaseName("ix_vehicle_tax_webhook_deliveries_created_at");
            e.ToTable(t => t.HasCheckConstraint("ck_vehicle_tax_webhook_deliveries_attempt", "attempt >= 0"));
        });

        // Épica #14644 (HU #14647): contingencia de proveedor RUNT, global de plataforma (sin tenant_id: la empresa no la ve).
        modelBuilder.Entity<ContingenciaProveedorRunt>(e =>
        {
            e.ToTable("runt_contingencies", t =>
            {
                t.HasComment("Contingencia de proveedor RUNT por tipo de consulta. Global de plataforma, sin tenant_id: la empresa no la ve. provider nulo = sin contingencia. Épica #14644.");
                t.HasCheckConstraint("ck_runt_contingencies_kind_provider",
                    "provider IS NULL OR (kind IN ('vehicle_vin', 'vehicle_plate') AND provider IN ('kyverum_runt', 'verifik')) "
                    + "OR (kind = 'conductor' AND provider IN ('kyverum_runt_conductor', 'verifik_conductor'))");
                t.HasCheckConstraint("ck_runt_contingencies_kind", "kind IN ('vehicle_vin', 'vehicle_plate', 'conductor')");
            });
            e.HasKey(c => c.Id).HasName("pk_runt_contingencies");
            e.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(c => c.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            e.Property(c => c.Provider).HasColumnName("provider").HasMaxLength(40);
            e.Property(c => c.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired();
            e.Property(c => c.UpdatedByName).HasColumnName("updated_by_name").HasMaxLength(200).IsRequired();
            e.Property(c => c.UpdatedById).HasColumnName("updated_by_id");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(c => c.Kind).IsUnique().HasDatabaseName("uq_runt_contingencies_kind");
        });

        modelBuilder.Entity<ValorMockAvaluo>(e =>
        {
            e.ToTable("valores_mock_avaluo");
            e.HasKey(v => v.Id).HasName("pk_valores_mock_avaluo");
            e.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(v => v.Clave).HasColumnName("clave").HasMaxLength(32).IsRequired();
            e.Property(v => v.Fuente).HasColumnName("fuente").HasMaxLength(30).IsRequired();
            e.Property(v => v.ValorCop).HasColumnName("valor_cop").HasColumnType("numeric(15,2)");
            e.HasIndex(v => new { v.Clave, v.Fuente }).IsUnique().HasDatabaseName("uq_valores_mock_avaluo");
        });

        // Llaves de Data Protection del servicio (cifran los secretos de los avisos), en su esquema.
        modelBuilder.Entity<DataProtectionKey>(e => e.ToTable("data_protection_keys"));
    }

    public DbSet<ConsumoConsulta> Consumos => Set<ConsumoConsulta>();

    public DbSet<ValidacionKyverum> ValidacionesKyverum => Set<ValidacionKyverum>();

    public DbSet<AvisoProveedor> Avisos => Set<AvisoProveedor>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<ConfiguracionEmpresa> ConfiguracionEmpresas => Set<ConfiguracionEmpresa>();

    public DbSet<ValorMockAvaluo> ValoresMockAvaluo => Set<ValorMockAvaluo>();

    public DbSet<DepartamentoConsultaImpuesto> DepartamentosImpuesto => Set<DepartamentoConsultaImpuesto>();

    public DbSet<TrabajoImpuestoExterno> TrabajosImpuesto => Set<TrabajoImpuestoExterno>();

    public DbSet<ContingenciaProveedorRunt> ContingenciasRunt => Set<ContingenciaProveedorRunt>();

    public DbSet<EntregaAvisoImpuesto> EntregasAvisoImpuesto => Set<EntregaAvisoImpuesto>();

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
