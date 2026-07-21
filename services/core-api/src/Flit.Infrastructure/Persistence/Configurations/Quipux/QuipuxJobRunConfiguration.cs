using Flit.Infrastructure.Persistence.Schemas;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Quipux;

/// <summary>Mapea <c>admin.quipux_job_runs</c> (bitácora por ejecución del worker).</summary>
internal sealed class QuipuxJobRunConfiguration : IEntityTypeConfiguration<QuipuxJobRun>
{
    public void Configure(EntityTypeBuilder<QuipuxJobRun> builder)
    {
        // DDL gestionado por migración SQL cruda (32-HU10710-quipux-integracion.sql).
        builder.ToTable("quipux_job_runs", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.JobName).HasColumnName("job_name").HasMaxLength(40).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").HasDefaultValueSql("now()");
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");
        builder.Property(x => x.ClaimedCount).HasColumnName("claimed_count").IsRequired();
        builder.Property(x => x.OkCount).HasColumnName("ok_count").IsRequired();
        builder.Property(x => x.ErrorCount).HasColumnName("error_count").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);

        builder.HasIndex(x => new { x.JobName, x.StartedAt });
    }
}
